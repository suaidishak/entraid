namespace Lss.EntraLoginTest.Ingestion;

/// <summary>Data feeds exposed by an LSS plant API.</summary>
public enum FeedKind
{
    Rolling24,
    DayAhead,
    WeekAhead,
    FourMonthsAhead,
    Mmf
}

public static class FeedKinds
{
    /// <summary>DDQ forecast feeds (everything except MMF).</summary>
    public static readonly FeedKind[] Forecast =
    [
        FeedKind.Rolling24,
        FeedKind.DayAhead,
        FeedKind.WeekAhead,
        FeedKind.FourMonthsAhead
    ];

    public static readonly FeedKind[] All =
    [
        FeedKind.Rolling24,
        FeedKind.DayAhead,
        FeedKind.WeekAhead,
        FeedKind.FourMonthsAhead,
        FeedKind.Mmf
    ];

    /// <summary>Stable code stored in the database and used in URLs.</summary>
    public static string ToCode(this FeedKind kind) => kind switch
    {
        FeedKind.Rolling24 => "Rolling24",
        FeedKind.DayAhead => "DayAhead",
        FeedKind.WeekAhead => "WeekAhead",
        FeedKind.FourMonthsAhead => "FourMonthsAhead",
        FeedKind.Mmf => "Mmf",
        _ => kind.ToString()
    };

    /// <summary>File/segment name used by the sample API (lower case, no spaces).</summary>
    public static string ToSampleName(this FeedKind kind) => kind switch
    {
        FeedKind.Rolling24 => "rolling24",
        FeedKind.DayAhead => "dayahead",
        FeedKind.WeekAhead => "weekahead",
        FeedKind.FourMonthsAhead => "fourmonthsahead",
        FeedKind.Mmf => "mmf",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static bool TryParseCode(string? code, out FeedKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(code)) return false;
        foreach (var candidate in All)
            if (string.Equals(candidate.ToCode(), code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.ToSampleName(), code, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        return false;
    }
}
