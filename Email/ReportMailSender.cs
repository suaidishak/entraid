using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Identity.Web;

namespace Lss.EntraLoginTest.Email;

public sealed record ReportEmail(string Recipient, string Subject, string Body, string FileName, byte[] Content)
{
    public const int MaxAttachmentBytes = 2 * 1024 * 1024;
    public bool IsValid() =>
        MailAddress.TryCreate(Recipient, out var address) &&
        address.Address == Recipient && !Recipient.Any(char.IsWhiteSpace) &&
        Recipient.Length <= 254 && !string.IsNullOrWhiteSpace(Subject) &&
        Subject.Length <= 200 && !Subject.Contains('\r') && !Subject.Contains('\n') &&
        Body.Length <= 5000 && Content.Length is > 0 and <= MaxAttachmentBytes &&
        !string.IsNullOrWhiteSpace(FileName) && FileName.Length <= 200 &&
        !FileName.Any(char.IsControl);
}
public interface IReportMailSender
{
    Task SendAsync(ReportEmail email, CancellationToken cancellationToken);
}
public sealed class MailSubmissionException : Exception
{
    public MailSubmissionException() : base("Microsoft did not confirm acceptance of the message.") { }
}
public sealed class GraphReportMailSender(ITokenAcquisition tokens, IHttpClientFactory clients) : IReportMailSender
{
    public const string Scope = "https://graph.microsoft.com/.default";
    public const string SenderMailbox = "suaid.ishak@sae-malaysia.com";

    public async Task SendAsync(ReportEmail email, CancellationToken cancellationToken)
    {
        if (!email.IsValid()) throw new ArgumentException("Invalid report email.");
        var token = await tokens.GetAccessTokenForAppAsync(Scope);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(SenderMailbox)}/sendMail");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject = email.Subject,
                body = new { contentType = "Text", content = email.Body },
                toRecipients = new[] { new { emailAddress = new { address = email.Recipient } } },
                attachments = new[] { new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = email.FileName,
                    ["contentType"] = "application/octet-stream",
                    ["contentBytes"] = Convert.ToBase64String(email.Content)
                }}
            },
            saveToSentItems = true
        });
        // No automatic retries: repeating an ambiguous send could produce duplicate email.
        using var response = await clients.CreateClient("ReportMail").SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Accepted) throw new MailSubmissionException();
    }
}

