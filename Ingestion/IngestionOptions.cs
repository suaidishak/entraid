namespace Lss.EntraLoginTest.Ingestion;

/// <summary>Configuration for the API collection / ingestion module (section "Ingestion").</summary>
public sealed class IngestionOptions
{
    /// <summary>"Sample" (local files) or "Http" (live endpoints).</summary>
    public string Mode { get; set; } = "Sample";

    public string BaseAddress { get; set; } = "https://suaidishak.pythonanywhere.com";

    public string ForecastPath { get; set; } = "/api/v1/sites/{site}/{feed}";

    public string MmfPath { get; set; } = "/api/v1/sites/{site}/mmf";

    /// <summary>Root of the local sample-data folder used when Mode = Sample.</summary>
    public string? SampleDataRoot { get; set; }

    /// <summary>
    /// Optional zip (relative to <see cref="SampleDataRoot"/>) to read DDQ forecasts from first.
    /// Set to "lss-anomaly-test-2026-09-24.zip" to exercise anomaly detection against injected faults.
    /// </summary>
    public string? SampleForecastZip { get; set; }

    /// <summary>Optional explicit site list. When empty the site list is discovered.</summary>
    public string[] Sites { get; set; } = [];

    /// <summary>Run collection automatically on the configured interval.</summary>
    public bool AutoEnabled { get; set; }

    public int AutoIntervalMinutes { get; set; } = 60;

    /// <summary>Values above this (MW) are flagged as out of range.</summary>
    public decimal MaxForecastMw { get; set; } = 500m;

    /// <summary>Absolute MMF value above this is flagged as implausible.</summary>
    public decimal MmfOutOfRangeAbs { get; set; } = 100_000m;

    public bool IsSampleMode => !string.Equals(Mode, "Http", StringComparison.OrdinalIgnoreCase);

    public string ForecastUrl(string site, FeedKind feed) =>
        Combine(BaseAddress, ForecastPath.Replace("{site}", site, StringComparison.OrdinalIgnoreCase)
            .Replace("{feed}", feed.ToSampleName(), StringComparison.OrdinalIgnoreCase));

    public string MmfUrl(string site) =>
        Combine(BaseAddress, MmfPath.Replace("{site}", site, StringComparison.OrdinalIgnoreCase));

    private static string Combine(string baseAddress, string path) =>
        baseAddress.TrimEnd('/') + "/" + path.TrimStart('/');
}
