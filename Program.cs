using Lss.EntraLoginTest;
using Lss.EntraLoginTest.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Lss.EntraLoginTest.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Lss.EntraLoginTest.Email;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.GetConnectionString("LssDatabase") is null)
    builder.Configuration.AddJsonFile("database-settings.json", optional: false);
if (builder.Environment.IsDevelopment())
{
    // Keep local authentication callbacks on HTTPS even if an IDE overrides ASPNETCORE_URLS.
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(7020, listener => listener.UseHttps());
        options.ListenLocalhost(5245);
    });
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 7020);
}
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, RegistryRevalidation>();
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();
builder.Services.AddHttpClient("ReportMail", client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<IReportMailSender, GraphReportMailSender>();
builder.Services.AddSingleton<ReportDirectory>();
builder.Services.AddSingleton<UserRegistry>();
builder.Services.AddSingleton<IAuthorizationHandler, RegistryAuthorization>();
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().AddRequirements(new RegistryRequirement()).Build();
    options.AddPolicy("SignedIn", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser()
        .AddRequirements(new RegistryRequirement(AdminOnly: true)));
});
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    var previous = options.Events.OnTicketReceived;
    options.Events.OnTicketReceived = async context =>
    {
        await previous(context);
        if (context.Principal is not null)
            context.HttpContext.RequestServices.GetRequiredService<UserRegistry>()
                .RecordActivity(context.Principal, signIn: true);
    };
});
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();
builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.Cookie.Name = "__Host-Lss.EntraLoginTest";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Events.OnRedirectToAccessDenied = context =>
        {
            var registry = context.HttpContext.RequestServices.GetRequiredService<UserRegistry>();
            context.Response.Redirect(registry.Current(context.HttpContext.User)?.Role switch
            {
                AppRole.Revoked => "/revoked",
                null or AppRole.Pending => "/pending",
                _ => "/access-denied"
            });
            return Task.CompletedTask;
        };
    });
var setup = new EntraSetup(builder.Configuration);
builder.Services.AddSingleton(setup);

var app = builder.Build();
if (setup.IsConfigured) app.Services.GetRequiredService<UserRegistry>().VerifySetup();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
// Preserve authorization/form error status codes instead of re-executing POSTs as Razor pages.
app.UseStatusCodePages("text/plain", "The request could not be completed (HTTP {0}).");
app.UseHttpsRedirection();
// Keep the landing page usable with placeholders, without contacting Entra.
app.Use(async (context, next) =>
{
    if (!setup.IsConfigured &&
        (context.Request.Path.StartsWithSegments("/MicrosoftIdentity") ||
         context.Request.Path.StartsWithSegments("/protected")))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("Entra configuration is incomplete. Follow README.md, set AzureAd credentials, and restart the app.");
        return;
    }
    await next(context);
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var registry = context.RequestServices.GetRequiredService<UserRegistry>();
        registry.RecordActivity(context.User);
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path == "/" || context.Request.Path == "/access")
        {
            context.Response.Redirect(registry.Current(context.User)?.Role switch
            {
                AppRole.Admin => "/admin",
                AppRole.Staff => "/protected",
                AppRole.Revoked => "/revoked",
                _ => "/pending"
            });
            return;
        }
    }
    await next(context);
});
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorPages();
app.MapGet("/access", () => Results.Redirect("/")).RequireAuthorization("SignedIn");
app.MapPost("/admin/role", async (HttpContext context, UserRegistry registry, IAntiforgery antiforgery) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync();
        if (!Enum.TryParse<AppRole>(form["role"], out var role) || !Enum.IsDefined(role))
            return Results.BadRequest("Unknown role.");
        if (!long.TryParse(form["version"], out var version)) return Results.BadRequest("Reload the user list before saving.");
        registry.ChangeRole(context.User, form["userKey"].ToString(), role, version);
        return Results.Redirect("/admin?saved=true");
    }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid form token. Reload the page and try again."); }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
}).RequireAuthorization("Admin");
app.MapPost("/admin/transfer", async (HttpContext context, UserRegistry registry, IAntiforgery antiforgery) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync();
        if (form["confirm"] != "yes" || !long.TryParse(form["version"], out var version))
            return Results.BadRequest("Confirm administrator transfer and reload the user list.");
        registry.TransferAdmin(context.User, form["userKey"].ToString(), version);
        return Results.Redirect("/access");
    }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid form token. Reload and try again."); }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
}).RequireAuthorization("Admin");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();


public partial class Program { }

