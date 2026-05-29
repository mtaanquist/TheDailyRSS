using System.Net;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]       // loopback
    [InlineData("10.1.2.3")]        // private
    [InlineData("172.16.5.4")]      // private
    [InlineData("192.168.0.1")]     // private
    [InlineData("169.254.169.254")] // cloud metadata link-local
    [InlineData("100.64.0.1")]      // CGNAT
    [InlineData("0.0.0.0")]         // this-network
    [InlineData("::1")]             // IPv6 loopback
    [InlineData("fd00::1")]         // IPv6 unique-local
    [InlineData("fe80::1")]         // IPv6 link-local
    public void Blocks_non_public(string ip) => Assert.False(SsrfGuard.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]        // example.com
    [InlineData("2606:4700:4700::1111")] // public IPv6
    public void Allows_public(string ip) => Assert.True(SsrfGuard.IsPublic(IPAddress.Parse(ip)));
}
