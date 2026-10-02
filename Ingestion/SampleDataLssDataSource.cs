using System.IO.Compression;

namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Reads the API payloads from the local sample-data folder (and its archives)
/// so the collection module can be exercised without calling live endpoints.
/// Layout:
///   {root}/{feed}                                  (LSS-001 top-level templates)
///   {root}/lss-sites-002-044/LSS-xxx/{feed}
///   {root}/{site}/mmf.json  or {root}/mmf/{site}/mmf.json
///   {root}/lss-sites-002-044.zip, {root}/lss-anomaly-test-*.zip, {root}/lss-mmf-sites-*.zip
/// </summary>
public sealed class SampleDataLssDataSource(IngestionOptions options) : ILssDataSource
{
    private const string SiteDataFolder = "lss-sites-002-044";
    private const string AnomalyZip = "lss-anomaly-test-2026-09-24.zip";
    private const string SitesZip = "lss-sites-002-044.zip";
    private const string MmfZip = "lss-mmf-sites-001-044-2026-09-24.zip";

    public string Mode => "Sample";

    private string Root => options.SampleDataRoot
        ?? throw new InvalidOperationException("Ingestion:SampleDataRoot must be set when Mode is Sample.");

    public IReadOnlyList<string> DiscoverSites()
    {
        if (options.Sites.Length > 0) return options.Sites;

        var sites = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = Path.Combine(Root, SiteDataFolder);
        if (Directory.Exists(folder))
            foreach (var directory in Directory.EnumerateDirectories(folder))
                sites.Add(Path.GetFileName(directory));

        if (File.Exists(Path.Combine(Root, "dayahead"))) sites.Add("LSS-001");

        foreach (var zip in new[] { Path.Combine(Root, MmfZip), Path.Combine(Root, SitesZip), Path.Combine(Root, AnomalyZip) })
            if (File.Exists(zip))
                using (var archive = ZipFile.OpenRead(zip))
                    foreach (var entry in archive.Entries)
                    {
                        var slash = entry.FullName.IndexOf('/');
                        if (slash > 0) sites.Add(entry.FullName[..slash]);
                    }

        return sites.Count > 0 ? sites.ToArray() : Enumerable.Range(1, 44).Select(i => $"LSS-{i:000}").ToArray();
    }

    public Task<string> GetForecastAsync(string siteCode, FeedKind feed, CancellationToken cancellationToken)
    {
        var name = feed.ToSampleName();

        if (!string.IsNullOrWhiteSpace(options.SampleForecastZip))
        {
            var injected = ReadZipEntry(Path.Combine(Root, options.SampleForecastZip), $"{siteCode}/{name}");
            if (injected is not null) return Task.FromResult(injected);
        }

        var candidates = new[]
        {
            Path.Combine(Root, siteCode, name),
            Path.Combine(Root, SiteDataFolder, siteCode, name),
            Path.Combine(Root, "lss-sites-001-044", siteCode, name)
        };
        foreach (var path in candidates)
            if (File.Exists(path)) return File.ReadAllTextAsync(path, cancellationToken);

        if (string.Equals(siteCode, "LSS-001", StringComparison.OrdinalIgnoreCase))
        {
            var template = Path.Combine(Root, name);
            if (File.Exists(template)) return File.ReadAllTextAsync(template, cancellationToken);
        }

        foreach (var zip in new[] { Path.Combine(Root, SitesZip), Path.Combine(Root, AnomalyZip) })
        {
            var content = ReadZipEntry(zip, $"{siteCode}/{name}");
            if (content is not null) return Task.FromResult(content);
        }

        throw new FileNotFoundException($"No sample forecast file found for {siteCode}/{name} under '{Root}'.");
    }

    public Task<string> GetMmfAsync(string siteCode, CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            Path.Combine(Root, siteCode, "mmf.json"),
            Path.Combine(Root, "mmf", siteCode, "mmf.json"),
            Path.Combine(Root, "lss-mmf-sites-001-044", siteCode, "mmf.json")
        };
        foreach (var path in candidates)
            if (File.Exists(path)) return File.ReadAllTextAsync(path, cancellationToken);

        var content = ReadZipEntry(Path.Combine(Root, MmfZip), $"{siteCode}/mmf.json");
        if (content is not null) return Task.FromResult(content);

        throw new FileNotFoundException($"No sample MMF file found for {siteCode} under '{Root}'.");
    }

    public string Describe(string siteCode, FeedKind feed) =>
        feed == FeedKind.Mmf
            ? Path.Combine(Root, MmfZip, siteCode, "mmf.json")
            : Path.Combine(Root, SiteDataFolder, siteCode, feed.ToSampleName());

    private static string? ReadZipEntry(string zipPath, string entryName)
    {
        if (!File.Exists(zipPath)) return null;
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName);
        if (entry is null) return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
