using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lss.EntraLoginTest.Ingestion;

[Authorize(Policy = "Admin")]
[Route("admin/ingestion")]
public sealed class IngestionController(IngestionService ingestion, ILogger<IngestionController> logger) : Controller
{
    [HttpPost("run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ingestion.RunAsync(FeedKinds.All, "Manual", User.Identity?.Name, cancellationToken);
            if (result is null) return Redirect("/admin/ingestion?status=busy");
            return Redirect("/admin/ingestion?status=done" +
                $"&run={result.RunId}&result={Uri.EscapeDataString(result.Status)}" +
                $"&readings={result.ReadingsInserted}&anomalies={result.AnomaliesInserted}");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Manual LSS collection failed.");
            return Redirect("/admin/ingestion?status=error&detail=" + Uri.EscapeDataString(exception.Message));
        }
    }
}
