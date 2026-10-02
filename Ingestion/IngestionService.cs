using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Collects DDQ and MMF data from the configured source, detects anomalies and
/// stores everything in the repository. One run can cover many sites and feeds;
/// a failure on one endpoint is recorded as an anomaly without aborting the run.
/// </summary>
public sealed class IngestionService(
    ILssDataSource source,
    AnomalyDetector detector,
    IngestionRepository repository,
    IOptions<IngestionOptions> optionsAccessor,
    ILogger<IngestionService> logger)
{
    private readonly IngestionOptions options = optionsAccessor.Value;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Runs a collection. Returns null when another run is already in progress.</summary>
    public async Task<IngestionRunResult?> RunAsync(IReadOnlyList<FeedKind> feeds, string trigger,
        string? requestedBy, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return null;

        var stopwatch = Stopwatch.StartNew();
        var runId = repository.BeginRun(trigger, requestedBy);
        var sites = source.DiscoverSites();
        var siteIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ingestedAtUtc = DateTime.UtcNow;
        int attempted = 0, succeeded = 0, failed = 0, readings = 0, anomalies = 0;

        try
        {
            foreach (var site in sites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!siteIds.TryGetValue(site, out var siteId))
                    siteIds[site] = siteId = repository.EnsureSite(site, null);

                foreach (var feed in feeds)
                {
                    attempted++;
                    var location = source.Describe(site, feed);
                    try
                    {
                        if (feed == FeedKind.Mmf)
                        {
                            var parsed = MmfJsonParser.Parse(await source.GetMmfAsync(site, cancellationToken));
                            repository.EnsureEndpoint(siteId, feed, options.MmfUrl(site));
                            readings += (int)repository.InsertMmfReadings(runId, siteId, parsed, location, ingestedAtUtc);
                            anomalies += (int)repository.InsertAnomalies(runId, siteId, feed, detector.Inspect(parsed));
                        }
                        else
                        {
                            var parsed = ForecastXmlParser.Parse(await source.GetForecastAsync(site, feed, cancellationToken));
                            repository.EnsureEndpoint(siteId, feed, options.ForecastUrl(site, feed));
                            readings += (int)repository.InsertForecastReadings(runId, siteId, feed, parsed, location, ingestedAtUtc);
                            anomalies += (int)repository.InsertAnomalies(runId, siteId, feed, detector.Inspect(parsed));
                        }
                        succeeded++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failed++;
                        var code = exception is FormatException ? AnomalyCodes.ParseFailed : AnomalyCodes.RequestFailed;
                        anomalies += (int)repository.InsertAnomalies(runId, siteId, feed,
                            [new DataAnomaly(code, null, null, null, Trim($"{feed.ToCode()} for {site}: {exception.Message}", 400))]);
                        logger.LogWarning(exception, "LSS ingestion {Feed} for {Site} failed.", feed, site);
                    }
                }
            }

            var status = failed > 0 ? (succeeded > 0 ? "CompletedWithAnomalies" : "Failed")
                : anomalies > 0 ? "CompletedWithAnomalies" : "Completed";
            repository.CompleteRun(runId, status, attempted, succeeded, failed, readings, anomalies, null);
            logger.LogInformation(
                "LSS ingestion run {RunId} {Status}: {Succeeded}/{Attempted} endpoints, {Readings} readings, {Anomalies} anomalies.",
                runId, status, succeeded, attempted, readings, anomalies);
            stopwatch.Stop();
            return new IngestionRunResult(runId, status, attempted, succeeded, failed, readings, anomalies, stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            repository.CompleteRun(runId, "Failed", attempted, succeeded, failed, readings, anomalies, Trim(exception.Message, 1000));
            logger.LogError(exception, "LSS ingestion run {RunId} failed.", runId);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
