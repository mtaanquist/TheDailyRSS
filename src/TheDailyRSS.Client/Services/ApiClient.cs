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
    public Task<AuthResponse> LoginAsync(LoginRequest req) => PostAsync<AuthResponse>("api/auth/login", req);
    public Task LogoutAsync() => SendAsync(HttpMethod.Post, "api/auth/logout");
    public Task<UserDto> MeAsync() => GetAsync<UserDto>("api/auth/me");
    public Task<UserDto> UpdateProfileAsync(UpdateProfileRequest req) => PutAsync<UserDto>("api/auth/profile", req);
    public Task<UserDto> UpdatePreferencesAsync(PreferencesDto req) => PutAsync<UserDto>("api/auth/preferences", req);
    public Task ChangePasswordAsync(ChangePasswordRequest req) => SendAsync(HttpMethod.Post, "api/auth/password", req);
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
    public Task<AiSummaryDto> GenerateDailySummaryAsync(DateOnly date) => PostAsync<AiSummaryDto>($"api/ai/summary/daily/{D(date)}", new { });

    /// <summary>"The Weekly" — the current curated edition. GET returns null until it's been generated.</summary>
    public Task<WeeklyEditionDto?> GetWeeklyEditionAsync() => GetOrNullAsync<WeeklyEditionDto>("api/ai/weekly");
    public Task<WeeklyEditionDto> GenerateWeeklyEditionAsync() => PostAsync<WeeklyEditionDto>("api/ai/weekly", new { });

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
