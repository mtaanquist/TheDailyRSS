namespace TheDailyRSS.Client.Services;

/// <summary>Holds the current bearer token in memory so the message handler can read it cheaply.</summary>
public sealed class TokenStore
{
    public string? Token { get; set; }
}
