namespace TheDailyRSS.Shared;

/// <summary>A tracked ticker with its latest server-fetched quote, as seen by one reader. <see cref="Change"/>
/// and <see cref="ChangePercent"/> are the move since the previous close.</summary>
public sealed record TickerDto(
    string Symbol,
    string Name,
    string Currency,
    double Price,
    double PreviousClose,
    double Change,
    double ChangePercent,
    bool Promoted,
    int SortOrder,
    DateTimeOffset? UpdatedAt);

/// <summary>A symbol-search suggestion for the add-ticker autocomplete.</summary>
public sealed record TickerSearchResultDto(string Symbol, string Name, string Exchange, string Type);

public sealed class AddTickerRequest
{
    public string Symbol { get; set; } = "";
}

public sealed class UpdateTickerRequest
{
    /// <summary>Promote the ticker to the front-page bar (or demote it).</summary>
    public bool Promoted { get; set; }
}
