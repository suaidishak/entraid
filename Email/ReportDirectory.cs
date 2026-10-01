namespace Lss.EntraLoginTest.Email;

public sealed class ReportDirectory(IWebHostEnvironment environment)
{
    public const string Recipient = "suaid.ishak@sae-malaysia.com";
    // The content root is C:\Users\User\Documents\LSS\LSS-Entra-Login-Test locally.
    public string DirectoryPath { get; } = Path.Combine(environment.ContentRootPath, "reportsrepo");
    public SemaphoreSlim SendLock { get; } = new(1, 1);

    public async Task<ReportEmail?> ReadLatestAsync(CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(DirectoryPath);
        if (!directory.Exists) throw new DirectoryNotFoundException();
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The report directory must not be a link.");
        var file = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) == 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal).FirstOrDefault();
        if (file is null) return null;
        // Sharing mode rejects a file that is still being written by the report generator.
        await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > ReportEmail.MaxAttachmentBytes)
            throw new InvalidDataException("The report must be non-empty and no larger than 2 MB.");
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, cancellationToken);
        return new ReportEmail(Recipient, "Single Buyer LSS — Daily report",
            "Please find the daily LSS report attached.", file.Name, content.ToArray());
    }
}

