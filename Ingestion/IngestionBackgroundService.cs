using Microsoft.Extensions.Options;

namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Optional scheduled collection. Disabled unless Ingestion:AutoEnabled is true.
/// Manual collection is always available from the admin page.
/// </summary>
public sealed class IngestionBackgroundService(
    IngestionService ingestion,
    IOptions<IngestionOptions> optionsAccessor,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsAccessor.Value;
        if (!options.AutoEnabled)
        {
            logger.LogInformation("Automatic LSS collection is disabled (Ingestion:AutoEnabled=false).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.AutoIntervalMinutes));
        logger.LogInformation("Automatic LSS collection enabled every {Minutes} minute(s).", interval.TotalMinutes);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ingestion.RunAsync(FeedKinds.All, "Automatic", "scheduler", stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic LSS collection failed.");
            }
        }
    }
}
