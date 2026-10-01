using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
namespace Lss.EntraLoginTest.Access;

// Protected interactions also check the database immediately; this updates idle circuits.
public sealed class RegistryRevalidation(ILoggerFactory logger, UserRegistry registry)
    : RevalidatingServerAuthenticationStateProvider(logger)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        try { return Task.FromResult(registry.Current(state.User)?.Role is AppRole.Staff or AppRole.Admin); }
        catch { return Task.FromResult(false); } // Fail closed if the database becomes unavailable.
    }
}

