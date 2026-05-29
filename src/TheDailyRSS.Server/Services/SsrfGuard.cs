using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Builds <see cref="SocketsHttpHandler"/>s that refuse to connect to non-public IP addresses,
/// closing the SSRF hole opened by fetching user-supplied feed/discovery/AI URLs server-side.
///
/// <para>The check runs in <see cref="SocketsHttpHandler.ConnectCallback"/>, so it covers the
/// initial request <em>and every redirect hop</em>, and it connects to the exact IP it vetted —
/// defeating DNS-rebinding (a TOCTOU where the name resolves to a public IP at check time and a
/// private one at connect time).</para>
/// </summary>
public static class SsrfGuard
{
    public static SocketsHttpHandler CreateHandler(bool allowAutoRedirect, int maxRedirects = 0) => new()
    {
        AllowAutoRedirect = allowAutoRedirect,
        MaxAutomaticRedirections = maxRedirects > 0 ? maxRedirects : 1,
        ConnectCallback = GuardedConnectAsync,
    };

    private static async ValueTask<Stream> GuardedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] resolved = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        var allowed = resolved.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException(
                $"Refusing to connect to '{host}': resolves only to a private, loopback or link-local address.");

        Exception? last = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                last = ex;
                socket.Dispose();
            }
        }
        throw last ?? new HttpRequestException($"Could not connect to '{host}'.");
    }

    /// <summary>True only for globally-routable unicast addresses; blocks loopback, link-local,
    /// the RFC1918 / RFC4193 private ranges, CGNAT, multicast and reserved space.</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;

        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] switch
            {
                0 => false,                                   // 0.0.0.0/8 "this network"
                10 => false,                                  // 10.0.0.0/8 private
                127 => false,                                 // 127.0.0.0/8 loopback
                169 when b[1] == 254 => false,                // 169.254.0.0/16 link-local (incl. cloud metadata)
                172 when b[1] is >= 16 and <= 31 => false,    // 172.16.0.0/12 private
                192 when b[1] == 168 => false,                // 192.168.0.0/16 private
                100 when b[1] is >= 64 and <= 127 => false,   // 100.64.0.0/10 CGNAT
                >= 224 => false,                              // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
                _ => true,
            };
        }

        // IPv6
        if (IPAddress.IPv6Any.Equals(ip)) return false;     // ::
        if ((b[0] & 0xFE) == 0xFC) return false;            // fc00::/7 unique-local
        return true;
    }
}
