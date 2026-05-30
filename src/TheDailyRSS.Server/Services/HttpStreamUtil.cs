namespace TheDailyRSS.Server.Services;

internal static class HttpStreamUtil
{
    /// <summary>Copies at most <paramref name="maxBytes"/> from <paramref name="source"/>, throwing if the
    /// source exceeds the cap. <see cref="HttpClient.MaxResponseContentBufferSize"/> does not apply to
    /// <c>ReadAsStreamAsync</c>, so the bound is enforced here for every streamed fetch.</summary>
    public static async Task<MemoryStream> ReadCappedAsync(Stream source, int maxBytes, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                await buffer.DisposeAsync();
                throw new InvalidOperationException($"Response exceeded the {maxBytes}-byte limit.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer;
    }
}
