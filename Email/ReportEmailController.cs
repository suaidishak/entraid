using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Client;

namespace Lss.EntraLoginTest.Email;

[Authorize(Policy = "Admin")]
[Route("admin/email")]
public sealed class ReportEmailController(IReportMailSender sender, ReportDirectory reports) : Controller
{
    [HttpPost("check-send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckAndSend(CancellationToken cancellationToken)
    {
        if (!await reports.SendLock.WaitAsync(0, cancellationToken)) return Result("busy");
        string? filename = null;
        try
        {
            var email = await reports.ReadLatestAsync(cancellationToken);
            if (email is null) return Result("empty");
            filename = email.FileName;
            await sender.SendAsync(email, cancellationToken);
            return Result("accepted", filename);
        }
        catch (DirectoryNotFoundException) { return Result("missing"); }
        catch (InvalidDataException) { return Result("invalid-file"); }
        catch (IOException) { return Result("file-error"); }
        catch (UnauthorizedAccessException) { return Result("file-error"); }
        catch (MsalException) { return Result("configuration", filename); }
        catch (ArgumentException) { return Result("invalid-file"); }
        catch (MailSubmissionException) { return Result("unconfirmed", filename); }
        catch (HttpRequestException) { return Result("unconfirmed", filename); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Result("unconfirmed", filename); }
        finally { reports.SendLock.Release(); }
    }

    private RedirectResult Result(string status, string? filename = null) =>
        Redirect("/admin/email?status=" + status + (filename is null ? "" : "&file=" + Uri.EscapeDataString(filename)));
}

