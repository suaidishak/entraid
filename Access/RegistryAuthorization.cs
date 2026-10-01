using Microsoft.AspNetCore.Authorization;
namespace Lss.EntraLoginTest.Access;

public sealed record RegistryRequirement(bool AdminOnly = false) : IAuthorizationRequirement;
public sealed class RegistryAuthorization(UserRegistry registry) : AuthorizationHandler<RegistryRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RegistryRequirement requirement)
    {
        var role = registry.Current(context.User)?.Role;
        if (role == AppRole.Admin || (!requirement.AdminOnly && role == AppRole.Staff))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}


