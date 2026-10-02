namespace Lss.EntraLoginTest.Ingestion;

/// <summary>Calls the live LSS plant endpoints over HTTP (GET, no API key).</summary>
public sealed class HttpLssDataSource(HttpClient client, IngestionOptions options) : ILssDataSource
{
    public string Mode => "Http";

    public async Task<string> GetForecastAsync(string siteCode, FeedKind feed, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(options.ForecastUrl(siteCode, feed), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetMmfAsync(string siteCode, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(options.MmfUrl(siteCode), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public string Describe(string siteCode, FeedKind feed) =>
        feed == FeedKind.Mmf ? options.MmfUrl(siteCode) : options.ForecastUrl(siteCode, feed);

    public IReadOnlyList<string> DiscoverSites() =>
        options.Sites.Length > 0 ? options.Sites : Enumerable.Range(1, 44).Select(i => $"LSS-{i:000}").ToArray();
}
