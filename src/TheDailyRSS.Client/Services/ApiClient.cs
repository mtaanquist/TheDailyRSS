using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Client.Services;

/// <summary>Thrown when the API returns a non-success status; carries a user-friendly message.</summary>
public sealed class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Typed wrapper around every server endpoint.</summary>
public sealed class ApiClient(HttpClient http)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ── Auth ──────────────────────────────────────────────────────
    public Task<AuthResponse> RegisterAsync(RegisterRequest req) => PostAsync<AuthResponse>("api/auth/register", req);
    public Task<LoginResponse> LoginAsync(LoginRequest req) => PostAsync<LoginResponse>("api/auth/login", req);
    public Task LogoutAsync() => SendAsync(HttpMethod.Post, "api/auth/logout");
    public Task<UserDto> MeAsync() => GetAsync<UserDto>("api/auth/me");
    public Task<TotpEnrollResponse> TotpEnrollAsync() => PostAsync<TotpEnrollResponse>("api/auth/totp/enroll", new { });
    public Task<TotpConfirmResponse> TotpConfirmAsync(string code) => PostAsync<TotpConfirmResponse>("api/auth/totp/confirm", new TotpConfirmRequest { Code = code });
    public Task<UserDto> TotpDisableAsync(string password) => PostAsync<UserDto>("api/auth/totp/disable", new TotpDisableRequest { Password = password });

    // ── Passkeys (#38) — the begin steps return the FIDO2 options as raw JSON for the browser ceremony ──
    public Task<List<PasskeyDto>> GetPasskeysAsync() => GetAsync<List<PasskeyDto>>("api/auth/passkeys");
    public Task RemovePasskeyAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/auth/passkeys/{id}");
    public Task<string> PasskeyRegisterBeginAsync() => PostForStringAsync("api/auth/passkeys/register/begin");
    public Task<PasskeyDto> PasskeyRegisterCompleteAsync(string responseJson, string? nickname) =>
        PostAsync<PasskeyDto>("api/auth/passkeys/register/complete", new PasskeyRegisterCompleteRequest { ResponseJson = responseJson, Nickname = nickname });
    public Task<string> PasskeyLoginBeginAsync() => PostForStringAsync("api/auth/passkeys/login/begin");
    public Task<LoginResponse> PasskeyLoginCompleteAsync(string handle, string responseJson) =>
        PostAsync<LoginResponse>("api/auth/passkeys/login/complete", new PasskeyLoginCompleteRequest { Handle = handle, ResponseJson = responseJson });

    private async Task<string> PostForStringAsync(string url)
    {
        var resp = await http.PostAsync(url, null);
        if (!resp.IsSuccessStatusCode) await ThrowAsync(resp);
        return await resp.Content.ReadAsStringAsync();
    }
    public Task<UserDto> UpdateProfileAsync(UpdateProfileRequest req) => PutAsync<UserDto>("api/auth/profile", req);
    public Task<UserDto> UpdatePreferencesAsync(PreferencesDto req) => PutAsync<UserDto>("api/auth/preferences", req);
    public Task ChangePasswordAsync(ChangePasswordRequest req) => SendAsync(HttpMethod.Post, "api/auth/password", req);
    public Task<UserDto> ChangeEmailAsync(ChangeEmailRequest req) => PostAsync<UserDto>("api/auth/email", req);
    public Task<List<SessionDto>> GetSessionsAsync() => GetAsync<List<SessionDto>>("api/auth/sessions");
    public Task RevokeSessionAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/auth/sessions/{id}");
    public Task RevokeOtherSessionsAsync() => SendAsync(HttpMethod.Post, "api/auth/sessions/revoke-others");
    public Task<SyncStatusDto> GetSyncStatusAsync() => GetAsync<SyncStatusDto>("api/auth/sync-status");

    // ── Categories (read) ─────────────────────────────────────────
    public Task<List<CategoryDto>> GetCategoriesAsync() => GetAsync<List<CategoryDto>>("api/categories");

    // ── Admin: category taxonomy ──────────────────────────────────
    public Task<List<CategoryDto>> GetAdminCategoriesAsync() => GetAsync<List<CategoryDto>>("api/admin/categories");
    public Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest req) => PostAsync<CategoryDto>("api/admin/categories", req);
    public Task UpdateCategoryAsync(Guid id, UpdateCategoryRequest req) => SendAsync(HttpMethod.Put, $"api/admin/categories/{id}", req);
    public Task DeleteCategoryAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/admin/categories/{id}");
    public Task ReorderCategoriesAsync(ReorderRequest req) => SendAsync(HttpMethod.Put, "api/admin/categories/reorder", req);

    // ── Admin: site settings ──────────────────────────────────────
    public Task<AiHouseStyleDto> GetAiHouseStyleAsync() => GetAsync<AiHouseStyleDto>("api/admin/settings/ai-house-style");
    public Task<AiHouseStyleDto> UpdateAiHouseStyleAsync(string? value) =>
        PutAsync<AiHouseStyleDto>("api/admin/settings/ai-house-style", new UpdateAiHouseStyleRequest { Value = value });

    public Task<List<AiJobDto>> GetAiJobsAsync() => GetAsync<List<AiJobDto>>("api/admin/ai-jobs");
    public Task<List<AiErrorDto>> GetAiErrorsAsync() => GetAsync<List<AiErrorDto>>("api/admin/ai-errors");

    // ── Keyword filters (mute words) ──────────────────────────────
    public Task<List<KeywordFilterDto>> GetKeywordsAsync() => GetAsync<List<KeywordFilterDto>>("api/keywords");
    public Task<KeywordFilterDto> AddKeywordAsync(CreateKeywordRequest req) => PostAsync<KeywordFilterDto>("api/keywords", req);
    public Task DeleteKeywordAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/keywords/{id}");

    // ── Field filters (mute by structured feed-item field) ────────
    public Task<List<FieldFilterDto>> GetFieldFiltersAsync() => GetAsync<List<FieldFilterDto>>("api/field-filters");
    public Task<FieldFilterDto> AddFieldFilterAsync(CreateFieldFilterRequest req) => PostAsync<FieldFilterDto>("api/field-filters", req);
    public Task DeleteFieldFilterAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/field-filters/{id}");

    // ── Feeds ─────────────────────────────────────────────────────
    public Task<List<FeedDto>> GetFeedsAsync(Guid? categoryId = null) =>
        GetAsync<List<FeedDto>>("api/feeds" + (categoryId is { } c ? $"?categoryId={c}" : ""));
    public Task<FeedDetectResult> DetectFeedAsync(AddFeedRequest req) => PostAsync<FeedDetectResult>("api/feeds/detect", req);
    public Task<FeedDto> AddFeedAsync(AddFeedRequest req) => PostAsync<FeedDto>("api/feeds", req);
    public Task UpdateFeedAsync(Guid id, UpdateFeedRequest req) => SendAsync(HttpMethod.Put, $"api/feeds/{id}", req);
    public Task DeleteFeedAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/feeds/{id}");
    public Task MoveFeedAsync(Guid id, MoveFeedRequest req) => SendAsync(HttpMethod.Post, $"api/feeds/{id}/move", req);
    public Task RefreshFeedAsync(Guid id) => SendAsync(HttpMethod.Post, $"api/feeds/{id}/refresh");
    public Task RefreshAllFeedsAsync() => SendAsync(HttpMethod.Post, "api/feeds/refresh");

    public async Task<OpmlImportResult> ImportOpmlAsync(string content)
    {
        using var body = new StringContent(content, System.Text.Encoding.UTF8, "text/x-opml");
        var resp = await http.PostAsync("api/opml", body);
        return await ReadAsync<OpmlImportResult>(resp);
    }
    public async Task<string> ExportOpmlAsync()
    {
        var resp = await http.GetAsync("api/opml");
        if (!resp.IsSuccessStatusCode) await ThrowAsync(resp);
        return await resp.Content.ReadAsStringAsync();
    }

    // ── Editions / articles ───────────────────────────────────────
    public Task<List<EditionDateDto>> GetEditionDatesAsync() => GetAsync<List<EditionDateDto>>("api/editions/dates");

    public Task<EditionDto> GetLatestEditionAsync(Guid? categoryId, Guid? sourceId, bool unreadOnly) =>
        GetAsync<EditionDto>($"api/editions/latest?{Query(categoryId, sourceId, null, unreadOnly)}");

    public Task<EditionDto> GetEditionAsync(DateOnly date, Guid? categoryId, Guid? sourceId, bool saved, bool unreadOnly, bool hidden = false) =>
        GetAsync<EditionDto>($"api/editions/{D(date)}?{Query(categoryId, sourceId, saved, unreadOnly, hidden)}");

    public Task<ArticleDto> GetArticleAsync(Guid id) => GetAsync<ArticleDto>($"api/articles/{id}");
    public Task<ArticleNeighborsDto> GetArticleNeighborsAsync(Guid id) => GetAsync<ArticleNeighborsDto>($"api/articles/{id}/neighbors");
    public Task<ArticleAiSummaryDto> SummarizeArticleAsync(Guid id) => PostAsync<ArticleAiSummaryDto>($"api/articles/{id}/summary", new { });
    public Task SetReadAsync(Guid id, bool value) => SendAsync(HttpMethod.Post, $"api/articles/{id}/read", new SetBool(value));
    public Task SetSavedAsync(Guid id, bool value) => SendAsync(HttpMethod.Post, $"api/articles/{id}/save", new SetBool(value));
    public Task SetHiddenAsync(Guid id, bool value) => SendAsync(HttpMethod.Post, $"api/articles/{id}/hide", new SetBool(value));
    public Task SetPositionAsync(Guid id, int percent) => SendAsync(HttpMethod.Post, $"api/articles/{id}/position", new SetPosition(percent));
    public Task MarkEditionReadAsync(DateOnly date, Guid? categoryId) =>
        SendAsync(HttpMethod.Post, $"api/editions/{D(date)}/mark-read" + (categoryId is { } c ? $"?categoryId={c}" : ""));

    // ── AI summaries (BYOK) ───────────────────────────────────────
    public Task<AiSettingsDto> GetAiSettingsAsync() => GetAsync<AiSettingsDto>("api/ai/settings");
    public Task<AiSettingsDto> UpdateAiSettingsAsync(UpdateAiSettingsRequest req) => PutAsync<AiSettingsDto>("api/ai/settings", req);

    public Task<AiSummaryDto?> GetDailySummaryAsync(DateOnly date) => GetOrNullAsync<AiSummaryDto>($"api/ai/summary/daily/{D(date)}");
    /// <summary>Enqueues a daily-briefing generation (runs in the background). Poll <see cref="GetDailySummaryAsync"/>
    /// and <see cref="GetAiActivityAsync"/> for the result; throws <see cref="ApiException"/> if not configured.</summary>
    public Task GenerateDailySummaryAsync(DateOnly date) => SendAsync(HttpMethod.Post, $"api/ai/summary/daily/{D(date)}");

    /// <summary>"The Weekly" — the AI review of the week containing <paramref name="anchor"/>
    /// (null = the current week). GET returns null until that week has been generated.</summary>
    public Task<AiSummaryDto?> GetWeeklyEditionAsync(DateOnly? anchor = null) =>
        GetOrNullAsync<AiSummaryDto>(anchor is { } a ? $"api/ai/weekly/{D(a)}" : "api/ai/weekly");
    /// <summary>Enqueues a Weekly generation (runs in the background). Poll <see cref="GetWeeklyEditionAsync"/>
    /// and <see cref="GetAiActivityAsync"/> for the result; throws <see cref="ApiException"/> if not configured.</summary>
    public Task GenerateWeeklyEditionAsync(DateOnly? anchor = null) =>
        SendAsync(HttpMethod.Post, anchor is { } a ? $"api/ai/weekly/{D(a)}" : "api/ai/weekly");

    /// <summary>The caller's in-flight AI generation (queued/running kinds) + their most recent error.</summary>
    public Task<AiActivityDto> GetAiActivityAsync() => GetAsync<AiActivityDto>("api/ai/activity");

    // ── Weather (issue #33) ───────────────────────────────────────
    /// <summary>The stored forecast for the reader's location on a date, or null when no location is set
    /// or nothing's on file yet (the server answers 204).</summary>
    public async Task<WeatherDto?> GetWeatherAsync(DateOnly date)
    {
        var resp = await http.GetAsync($"api/weather/{D(date)}");
        if (resp.StatusCode == HttpStatusCode.NoContent) return null;
        if (!resp.IsSuccessStatusCode) await ThrowAsync(resp);
        return await resp.Content.ReadFromJsonAsync<WeatherDto>();
    }

    /// <summary>Geocodes and saves the reader's weather location (empty clears it); returns the updated user.</summary>
    public Task<UserDto> SetWeatherLocationAsync(string query) =>
        PutAsync<UserDto>("api/weather/location", new SetWeatherLocationRequest { Query = query });

    // ── Tickers (issue #32) ───────────────────────────────────────
    public Task<List<TickerDto>> GetTickersAsync() => GetAsync<List<TickerDto>>("api/tickers");
    public Task<List<TickerSearchResultDto>> SearchTickersAsync(string q) =>
        GetAsync<List<TickerSearchResultDto>>($"api/tickers/search?q={Uri.EscapeDataString(q)}");
    public Task<TickerDto> AddTickerAsync(string symbol) => PostAsync<TickerDto>("api/tickers", new AddTickerRequest { Symbol = symbol });
    public Task<TickerDto> SetTickerPromotedAsync(string symbol, bool promoted) =>
        PutAsync<TickerDto>($"api/tickers/{Uri.EscapeDataString(symbol)}", new UpdateTickerRequest { Promoted = promoted });
    public Task RemoveTickerAsync(string symbol) => SendAsync(HttpMethod.Delete, $"api/tickers/{Uri.EscapeDataString(symbol)}");

    private static string Query(Guid? categoryId, Guid? sourceId, bool? saved, bool unreadOnly, bool hidden = false)
    {
        var parts = new List<string>();
        if (categoryId is { } c) parts.Add($"categoryId={c}");
        if (sourceId is { } s) parts.Add($"sourceId={s}");
        if (saved is true) parts.Add("saved=true");
        if (hidden) parts.Add("hidden=true");
        if (unreadOnly) parts.Add("unreadOnly=true");
        return string.Join("&", parts);
    }

    // ── plumbing ──────────────────────────────────────────────────
    private async Task<T> GetAsync<T>(string url) => await ReadAsync<T>(await http.GetAsync(url));

    /// <summary>GET that returns default(T) on 404 instead of throwing — for optional cached resources.</summary>
    private async Task<T?> GetOrNullAsync<T>(string url)
    {
        var resp = await http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.NotFound) return default;
        if (!resp.IsSuccessStatusCode) await ThrowAsync(resp);
        return await resp.Content.ReadFromJsonAsync<T>();
    }
    private async Task<T> PostAsync<T>(string url, object body) => await ReadAsync<T>(await http.PostAsJsonAsync(url, body));
    private async Task<T> PutAsync<T>(string url, object body) => await ReadAsync<T>(await http.PutAsJsonAsync(url, body));

    private async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) await ThrowAsync(resp);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) await ThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task ThrowAsync(HttpResponseMessage resp)
    {
        string message = resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your email or password didn't match.",
            HttpStatusCode.Forbidden => "You don't have access to that.",
            _ => $"Request failed ({(int)resp.StatusCode}).",
        };
        try
        {
            // The server returns RFC7807 ProblemDetails (detail/title); older responses used { error }.
            var payload = await resp.Content.ReadFromJsonAsync<ErrorPayload>();
            var detail = payload?.Detail ?? payload?.Error ?? payload?.Title;
            if (!string.IsNullOrWhiteSpace(detail)) message = detail;
        }
        catch { /* keep default */ }
        throw new ApiException(message, resp.StatusCode);
    }

    private sealed record ErrorPayload(string? Detail, string? Title, string? Error);
    private sealed record SetBool(bool Value);
    private sealed record SetPosition(int Percent);
}
