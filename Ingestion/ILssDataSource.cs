namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Source of raw DDQ (XML) and MMF (JSON) API responses. Implemented by the
/// local sample-data reader and the live HTTP client, so the ingestion service
/// is identical in both modes.
/// </summary>
public interface ILssDataSource
{
    string Mode { get; }

    Task<string> GetForecastAsync(string siteCode, FeedKind feed, CancellationToken cancellationToken);

    Task<string> GetMmfAsync(string siteCode, CancellationToken cancellationToken);

    /// <summary>A human-readable location for logs (URL or file path).</summary>
    string Describe(string siteCode, FeedKind feed);

    IReadOnlyList<string> DiscoverSites();
}
