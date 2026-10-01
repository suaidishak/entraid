namespace Lss.EntraLoginTest;

// Reports presence only; Entra verifies credentials during real sign-in.
public sealed class EntraSetup(IConfiguration configuration)
{
    public bool IsConfigured { get; } =
        Guid.TryParse(configuration["AzureAd:TenantId"], out var tenant) && tenant != Guid.Empty &&
        Guid.TryParse(configuration["AzureAd:ClientId"], out var client) && client != Guid.Empty &&
        !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"]) &&
        !configuration["AzureAd:ClientSecret"]!.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}

