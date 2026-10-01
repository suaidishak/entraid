using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lss.EntraLoginTest.Email;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Moq;

namespace AccessTests;

public class EmailTests
{
    [Fact]
    public async Task Folder_check_is_admin_only_and_reports_missing_empty_found_and_send_errors()
    {
        using var app = new TestApp();
        var sender = new CaptureSender();
        var reports = new ReportDirectory(app.RegistryEnvironment);
        using var host = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IReportMailSender>(sender);
            services.AddSingleton(reports);
        }));
        using var admin = host.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Add("X-Test-User", "admin");
        using var member = host.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        member.DefaultRequestHeaders.Add("X-Test-User", "member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/admin/email")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/admin/email/check-send", Form(""))).StatusCode);
        var page = await admin.GetStringAsync("/admin/email");
        Assert.Contains("Check reports and send", page);
        Assert.DoesNotContain("type=\"file\"", page);
        Assert.DoesNotContain("Connect Microsoft", page);
        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/admin/email/check-send", Form(""))).StatusCode);
        Assert.Contains("status=missing", (await admin.PostAsync("/admin/email/check-send", Form(token))).Headers.Location!.ToString());
        Directory.CreateDirectory(reports.DirectoryPath);
        Assert.Contains("status=empty", (await admin.PostAsync("/admin/email/check-send", Form(token))).Headers.Location!.ToString());
        var path = Path.Combine(reports.DirectoryPath, "daily.csv");
        await File.WriteAllTextAsync(path, "sample report");
        var response = await admin.PostAsync("/admin/email/check-send", Form(token));
        Assert.Contains("status=accepted", response.Headers.Location!.ToString());
        Assert.Contains("file=daily.csv", response.Headers.Location!.ToString());
        var message = Assert.Single(sender.Messages);
        Assert.Equal(ReportDirectory.Recipient, message.Recipient);
        Assert.Equal("daily.csv", message.FileName);
        Assert.Equal("sample report", System.Text.Encoding.UTF8.GetString(message.Content));
        Assert.True(File.Exists(path));
        sender.Fail = true;
        Assert.Contains("status=unconfirmed", (await admin.PostAsync("/admin/email/check-send", Form(token))).Headers.Location!.ToString());
        await reports.SendLock.WaitAsync();
        try { Assert.Contains("status=busy", (await admin.PostAsync("/admin/email/check-send", Form(token))).Headers.Location!.ToString()); }
        finally { reports.SendLock.Release(); }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/admin/email/connect")).StatusCode);
    }

    [Fact]
    public async Task Latest_file_is_selected_and_empty_or_oversized_files_are_rejected()
    {
        var reports = new ReportDirectory(new RegistryEnvironment());
        Directory.CreateDirectory(reports.DirectoryPath);
        var old = Path.Combine(reports.DirectoryPath, "old.csv");
        var latest = Path.Combine(reports.DirectoryPath, "latest.csv");
        await File.WriteAllTextAsync(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-1));
        await File.WriteAllTextAsync(latest, "latest");
        Assert.Equal("latest.csv", (await reports.ReadLatestAsync(default))!.FileName);
        await File.WriteAllBytesAsync(latest, []);
        await Assert.ThrowsAsync<InvalidDataException>(() => reports.ReadLatestAsync(default));
        await File.WriteAllBytesAsync(latest, new byte[ReportEmail.MaxAttachmentBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => reports.ReadLatestAsync(default));
    }

    [Fact]
    public async Task Graph_uses_application_token_fixed_mailbox_and_real_attachment_payload()
    {
        var tokens = new Mock<ITokenAcquisition>();
        tokens.Setup(x => x.GetAccessTokenForAppAsync(
            GraphReportMailSender.Scope, It.IsAny<string>(),
            It.IsAny<TokenAcquisitionOptions>())).ReturnsAsync("test-app-token");
        var handler = new GraphCapture();
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("ReportMail")).Returns(client);
        var service = new GraphReportMailSender(tokens.Object, factory.Object);
        var email = new ReportEmail(ReportDirectory.Recipient, "Daily report", "Attached report", "report.csv", [1, 2, 3]);
        await service.SendAsync(email, default);
        Assert.Equal("https://graph.microsoft.com/v1.0/users/suaid.ishak%40sae-malaysia.com/sendMail", handler.Url);
        Assert.Equal("Bearer test-app-token", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.True(json.RootElement.GetProperty("saveToSentItems").GetBoolean());
        var message = json.RootElement.GetProperty("message");
        Assert.Equal(ReportDirectory.Recipient, message.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        var attachment = message.GetProperty("attachments")[0];
        Assert.Equal("#microsoft.graph.fileAttachment", attachment.GetProperty("@odata.type").GetString());
        Assert.Equal("AQID", attachment.GetProperty("contentBytes").GetString());
        handler.Status = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<MailSubmissionException>(() => service.SendAsync(email, default));
    }

    private static FormUrlEncodedContent Form(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
}

public sealed class CaptureSender : IReportMailSender
{
    public List<ReportEmail> Messages { get; } = [];
    public bool Fail { get; set; }
    public Task SendAsync(ReportEmail email, CancellationToken cancellationToken)
    {
        if (Fail) throw new MailSubmissionException();
        Messages.Add(email);
        return Task.CompletedTask;
    }
}
public sealed class GraphCapture : HttpMessageHandler
{
    public string? Body { get; private set; }
    public string? Url { get; private set; }
    public string? Authorization { get; private set; }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.Accepted;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Url = request.RequestUri!.AbsoluteUri;
        Authorization = request.Headers.Authorization?.ToString();
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status);
    }
}


