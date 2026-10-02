using System.IO.Compression;
using System.Text.Json;
using Lss.EntraLoginTest.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessTests;

public class IngestionTests
{
    private const string ForecastXml = """
        <?xml version='1.0' encoding='utf-8'?>
        <PlantForecastIntervals>
          <PlantForecastIntervalNode>
            <IntervalStartTime>2026-09-24T00:00:00</IntervalStartTime>
            <IntervalEndTime>2026-09-24T00:15:00</IntervalEndTime>
            <IntervalLength>15</IntervalLength>
            <ForecastResultParameter>DayAhead</ForecastResultParameter>
            <ForecastValue>0.0</ForecastValue>
            <ValueUnit>MW</ValueUnit>
          </PlantForecastIntervalNode>
          <PlantForecastIntervalNode>
            <IntervalStartTime>2026-09-24T00:15:00</IntervalStartTime>
            <IntervalEndTime>2026-09-24T00:30:00</IntervalEndTime>
            <IntervalLength>15</IntervalLength>
            <ForecastResultParameter>DayAhead</ForecastResultParameter>
            <ForecastValue>19.5</ForecastValue>
            <ValueUnit>MW</ValueUnit>
          </PlantForecastIntervalNode>
        </PlantForecastIntervals>
        """;

    private const string MmfJson = """
        {"MMFResponse":{"PlantName":"LSS-002","MMFData":[
          {"Parameter":"AirPressure","Time":"2026-09-24T00:00:00","Unit":"hPa","MMF1":"1006.157","MMF2":"1006.200"},
          {"Parameter":"AirTemperature","Time":"2026-09-24T00:00:00","Unit":"C","MMF1":"27.1"}
        ]}}
        """;

    [Fact]
    public void Forecast_parser_reads_intervals_parameters_and_values()
    {
        var feed = ForecastXmlParser.Parse(ForecastXml);
        Assert.Equal(2, feed.Intervals.Count);
        Assert.Equal(new DateTime(2026, 9, 24, 0, 15, 0), feed.Intervals[1].StartLocal);
        Assert.Equal(19.5m, feed.Intervals[1].Value);
        Assert.Equal(15, feed.Intervals[0].LengthMinutes);
        Assert.Equal("DayAhead", feed.Intervals[0].Parameter);
        Assert.Throws<FormatException>(() => ForecastXmlParser.Parse("<PlantForecastIntervals>"));
    }

    [Fact]
    public void Mmf_parser_reads_parameters_and_series()
    {
        var feed = MmfJsonParser.Parse(MmfJson);
        Assert.Equal("LSS-002", feed.PlantName);
        Assert.Equal(3, feed.Readings.Count);
        Assert.Contains(feed.Readings, r => r.Parameter == "AirPressure" && r.SeriesIndex == 2 && r.Value == 1006.200m);
        Assert.Contains(feed.Readings, r => r.Parameter == "AirTemperature" && r.SeriesIndex == 1);
    }

    [Fact]
    public void Anomaly_detector_flags_null_missing_negative_and_all_zero_values()
    {
        var detector = new AnomalyDetector(new IngestionOptions());
        var start = new DateTime(2026, 9, 24, 0, 0, 0);
        var intervals = new List<ForecastInterval>
        {
            new(1, "", null, null, "15", 15, "DayAhead", "", null, "MW"),
            new(2, "2026-09-24T00:15:00", start.AddMinutes(15), null, "15", 15, "DayAhead", "-1.5", -1.5m, "MW"),
            new(3, "2026-09-24T00:30:00", start.AddMinutes(30), null, "15", 15, "DayAhead", "10", 10m, "MW")
        };
        var anomalies = detector.Inspect(new ForecastFeed(null, intervals));
        Assert.Contains(anomalies, a => a.Code == AnomalyCodes.NullTimestamp && a.IntervalNumber == 1);
        Assert.Contains(anomalies, a => a.Code == AnomalyCodes.MissingValue && a.IntervalNumber == 1);
        Assert.Contains(anomalies, a => a.Code == AnomalyCodes.NegativeValue && a.IntervalNumber == 2);

        var zero = new List<ForecastInterval>
        {
            new(1, "2026-09-24T00:00:00", start, start.AddMinutes(15), "15", 15, "DayAhead", "0", 0m, "MW"),
            new(2, "2026-09-24T00:15:00", start.AddMinutes(15), start.AddMinutes(30), "15", 15, "DayAhead", "0", 0m, "MW")
        };
        Assert.Contains(detector.Inspect(new ForecastFeed(null, zero)), a => a.Code == AnomalyCodes.AllZero);
    }

    [Fact]
    public async Task Ingestion_persists_readings_anomalies_sites_and_run()
    {
        using var database = new TestDatabase();
        var options = new IngestionOptions { Mode = "Sample", SampleDataRoot = "unused" };
        var repository = new IngestionRepository(database.Configuration);
        var source = new FakeSource(ForecastXml, MmfJson);
        var service = new IngestionService(source, new AnomalyDetector(options), repository,
            Options.Create(options), NullLogger<IngestionService>.Instance);

        var result = await service.RunAsync([FeedKind.DayAhead, FeedKind.Mmf], "Manual", "tester", default);

        Assert.NotNull(result);
        Assert.True(result!.Status == "Completed",
            "unexpected status " + result.Status + " anomalies: " +
            string.Join(" | ", database.Strings("SELECT TOP 5 CAST(AnomalyCode AS nvarchar(30)) COLLATE DATABASE_DEFAULT + N' / ' + COALESCE(FieldName, N'-') + N' / ' + Detail FROM repo.DataAnomalies")));
        Assert.Equal(4, result.EndpointsAttempted);
        Assert.Equal(4, result.EndpointsSucceeded);
        Assert.Equal(0, result.EndpointsFailed);
        Assert.Equal(10, result.ReadingsInserted);
        Assert.Equal(0, result.AnomaliesInserted);
        Assert.Equal(1, database.Scalar("SELECT COUNT(*) FROM repo.IngestionRuns"));
        Assert.Equal(2, database.Scalar("SELECT COUNT(*) FROM repo.Sites"));
        Assert.Equal(4, database.Scalar("SELECT COUNT(*) FROM repo.ForecastReadings"));
        Assert.Equal(6, database.Scalar("SELECT COUNT(*) FROM repo.MmfReadings"));
        Assert.Equal(4, database.Scalar("SELECT COUNT(*) FROM repo.ApiEndpoints"));
    }

    [Fact]
    public async Task Sample_anomaly_dataset_manifest_faults_are_detected()
    {
        var root = Environment.GetEnvironmentVariable("LSS_SAMPLE_DATA_ROOT")
            ?? @"C:\Users\User\Documents\LSS\Sample Data";
        var zipPath = Path.Combine(root, "lss-anomaly-test-2026-09-24.zip");
        if (!File.Exists(zipPath)) return; // sample data not present on this machine

        using var archive = ZipFile.OpenRead(zipPath);
        var detector = new AnomalyDetector(new IngestionOptions { Mode = "Sample", SampleDataRoot = root });

        using var manifestStream = archive.GetEntry("anomaly_manifest.json")!.Open();
        using var manifest = await JsonDocument.ParseAsync(manifestStream);
        var entries = manifest.RootElement.GetProperty("anomalies").EnumerateArray().ToList();
        Assert.Equal(9, entries.Count);

        foreach (var entry in entries)
        {
            var site = entry.GetProperty("site").GetString()!;
            var feedName = entry.GetProperty("feed").GetString()!;
            Assert.True(FeedKinds.TryParseCode(feedName, out var feed));
            var interval = entry.GetProperty("interval_number").GetInt32();
            var type = entry.GetProperty("type").GetString()!;

            var detected = detector.Inspect(ForecastXmlParser.Parse(await ReadEntry(archive, $"{site}/{feedName}")));
            var expected = type == "null_timestamp" ? AnomalyCodes.NullTimestamp : AnomalyCodes.NegativeValue;
            Assert.Contains(detected, a => a.Code == expected && a.IntervalNumber == interval);
        }
    }

    private static async Task<string> ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeSource(string forecast, string mmf) : ILssDataSource
    {
        public string Mode => "Fake";
        public Task<string> GetForecastAsync(string siteCode, FeedKind feed, CancellationToken cancellationToken) =>
            Task.FromResult(forecast);
        public Task<string> GetMmfAsync(string siteCode, CancellationToken cancellationToken) =>
            Task.FromResult(mmf);
        public string Describe(string siteCode, FeedKind feed) => $"{siteCode}/{feed.ToCode()}";
        public IReadOnlyList<string> DiscoverSites() => ["LSS-001", "LSS-002"];
    }
}
