namespace TheDailyRSS.Client.Services;

/// <summary>Process-wide browser facts resolved once at startup (before the first render).</summary>
public static class BrowserEnv
{
    /// <summary>True for Safari/WebKit (which includes every iOS browser, all of which are WebKit
    /// under the hood). WebKit's native <c>loading="lazy"</c> doesn't reliably fire for images inside
    /// our non-document scroll container (<c>.tdr-main</c>), so on WebKit images must load eagerly;
    /// Chromium/Gecko keep lazy-loading, where it works correctly in nested scrollers.</summary>
    public static bool IsWebKit { get; set; }
}
