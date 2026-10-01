using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Lss.EntraLoginTest.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessTests;

public class AccessFlowTests
{
    [Fact]
    public async Task Development_redirects_http_signin_to_the_registered_https_origin()
    {
        using var app = new TestApp();
        using var development = app.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost:7020")
        });
        var response = await client.GetAsync("/MicrosoftIdentity/Account/SignIn");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost:7020/MicrosoftIdentity/Account/SignIn",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Approval_revocation_persistence_and_server_access_are_enforced()
    {
        using var app = new TestApp();
        using var admin = app.Client("admin");
        using var member = app.Client("member");
        using var outsider = app.Client("outsider");
        using var anonymous = app.Client(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/protected")).StatusCode);
        Assert.Equal("/admin", (await admin.GetAsync("/")).Headers.Location?.ToString());
        Assert.Equal("/pending", (await member.GetAsync("/")).Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/protected")).StatusCode);
        var page = await admin.GetStringAsync("/admin");
        Assert.Contains("member@example.com", page);
        Assert.Contains("Active administrator", page);
        var registry = app.Services.GetRequiredService<UserRegistry>();
        var memberKey = registry.Current(TestIdentity.User("member"))!.Key;

        var missingToken = await admin.PostAsync("/admin/role", Form(memberKey, "Staff"));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        var forged = await member.PostAsync("/admin/role", Form(memberKey, "Admin"));
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);

        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        var approved = await admin.PostAsync("/admin/role", Form(memberKey, "Staff", token));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/admin")).StatusCode);
        Assert.Equal("/protected", (await member.GetAsync("/")).Headers.Location?.ToString());

        var restarted = new UserRegistry(app.Database.Configuration);
        Assert.Equal(AppRole.Staff, restarted.Current(TestIdentity.User("member"))!.Role);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync("/admin/role", Form(memberKey, "Revoked", token))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,
            (await admin.PostAsync("/admin/role", Form(memberKey, "Revoked", token, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/protected")).StatusCode);
        registry.RecordActivity(TestIdentity.User("member"), true);
        Assert.Equal(AppRole.Revoked, restarted.Current(TestIdentity.User("member"))!.Role);
        Assert.Equal("/revoked", (await member.GetAsync("/")).Headers.Location?.ToString());
        registry.ChangeRole(TestIdentity.User("admin"), memberKey, AppRole.Staff, 3);
        var transfer = new FormUrlEncodedContent(new Dictionary<string,string> {
            ["userKey"]=memberKey, ["version"]="4", ["confirm"]="yes", ["__RequestVerificationToken"]=token });
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/admin/transfer", transfer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/admin")).StatusCode);
        registry.RecordActivity(TestIdentity.User("admin"), true);
        Assert.Equal(AppRole.Staff, restarted.Current(TestIdentity.User("admin"))!.Role);
        registry.VerifySetup();
        Assert.Equal(2, app.Database.Scalar("SELECT COUNT(*) FROM lss.AccessAudit WHERE ActionCode='AdminTransferred'"));
        Assert.Equal(1, app.Database.Scalar("SELECT COUNT(DISTINCT CorrelationId) FROM lss.AccessAudit WHERE ActionCode='AdminTransferred'"));
        Assert.Throws<ArgumentException>(() => registry.ChangeRole(TestIdentity.User("member"), memberKey, AppRole.Revoked, 5));

    }

    [Fact]
    public void Bootstrap_is_pinned_and_untrusted_identities_cannot_register()
    {
        using var app = new TestApp();
        var registry = app.Services.GetRequiredService<UserRegistry>();
        var admin = TestIdentity.User("admin");
        registry.RecordActivity(admin, true);
        var renamedAdmin = TestIdentity.User("admin", email: "renamed@example.com");
        registry.RecordActivity(renamedAdmin, true);
        Assert.Equal(AppRole.Admin, registry.Current(renamedAdmin)!.Role);
        var lookalike = TestIdentity.User("member", email: "suaid.ishak@sae-malaysia.com");
        registry.RecordActivity(lookalike, true);
        Assert.Equal(AppRole.Pending, registry.Current(lookalike)!.Role);
        registry.RecordActivity(TestIdentity.User("outsider"), true);
        Assert.Null(registry.Current(TestIdentity.User("outsider")));
        registry.RecordActivity(new ClaimsPrincipal(new ClaimsIdentity()), true);
        Assert.Equal(2, registry.List(admin).Count);
        Assert.Throws<UnauthorizedAccessException>(() => registry.ChangeRole(lookalike, registry.Current(lookalike)!.Key, AppRole.Staff, 1));
    }

    private static FormUrlEncodedContent Form(string key, string role, string? token = null, long version = 1) =>
        new(new Dictionary<string, string> { ["userKey"] = key, ["role"] = role, ["version"] = version.ToString(), ["__RequestVerificationToken"] = token ?? "" });
}

public static class TestIdentity
{
    public const string Tenant = "11111111-1111-1111-1111-111111111111";
    public static IConfiguration Configuration => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build();
    public static ClaimsPrincipal User(string identity, string? email = null)
    {
        var oid = identity == "admin" ? "22222222-2222-2222-2222-222222222222" : "33333333-3333-3333-3333-333333333333";
        return new(new ClaimsIdentity([
            new Claim("tid", identity == "outsider" ? "44444444-4444-4444-4444-444444444444" : Tenant),
            new Claim("oid", oid), new Claim(ClaimTypes.NameIdentifier, oid),
            new Claim("preferred_username", email ?? (identity == "admin" ? "suaid.ishak@sae-malaysia.com" : "member@example.com")),
            new Claim("name", identity)
        ], "Test"));
    }
}

public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(Request.Headers.TryGetValue("X-Test-User", out var user)
            ? AuthenticateResult.Success(new AuthenticationTicket(TestIdentity.User(user.ToString()), "Test"))
            : AuthenticateResult.NoResult());
}

public sealed class TestApp : WebApplicationFactory<Program>
{
    public TestDatabase Database { get; } = new();
    public IWebHostEnvironment RegistryEnvironment { get; } = new RegistryEnvironment();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:LssDatabase", Database.ConnectionString);
        builder.UseSetting("AzureAd:TenantId", TestIdentity.Tenant);
        builder.UseSetting("AzureAd:ClientId", "55555555-5555-5555-5555-555555555555");
        builder.UseSetting("AzureAd:ClientSecret", "isolated-test-value");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(new UserRegistry(Database.Configuration));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        });
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Database.Dispose();
    }
    public HttpClient Client(string? user)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (user is not null) client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }
}

public sealed class RegistryEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "AccessTests";
    public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "LssAccessTests", Guid.NewGuid().ToString("N"));
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

