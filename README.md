# Isolated Blazor .NET 10 Entra login test

A standalone ASP.NET Core Blazor Web App using Interactive Server rendering and Microsoft.Identity.Web / Microsoft.Identity.Web.UI. Authentication uses OpenID Connect authorization code flow and a server-managed authentication cookie. User approval and roles are stored in SQL Server LSSRepo; Microsoft Entra handles sign-in. The optional email module uses Microsoft Graph application Mail.Send permission.

## Run with placeholders

Install the .NET 10 SDK, then run from this directory:

```powershell
dotnet restore
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

Open https://localhost:7020. With placeholders, the landing page shows configuration instructions; sign-in and protected routes return HTTP 503. No credentials are needed to build or view this setup page. The trust command may show an operating-system confirmation dialog.

## Register a separate Entra application

1. In Microsoft Entra admin center, open **App registrations > New registration**. Give it a test-specific name and choose **Accounts in this organizational directory only**.
2. Add the **Web** platform (not SPA) with these redirect URIs:
   - `https://localhost:7020/signin-oidc`
   - `https://localhost:7020/signout-callback-oidc`
3. Set the front-channel logout URL to `https://localhost:7020/signout-oidc`.
4. Leave implicit access-token and ID-token grant checkboxes disabled. This app uses authorization code flow.
5. Copy the **Directory (tenant) ID** and **Application (client) ID**.
6. Under **Certificates & secrets**, create a development client secret. Copy its **Value**, not its secret ID.

Use a dedicated test registration. This project does not create or modify Entra resources. Tenant consent and user-assignment policies still apply. Login alone requires no Graph API access; see Email reports below for the mail permission.

## Configure locally

The checked-in `appsettings.json` contains placeholders. Keep the secret out of that file. A UserSecretsId is already configured in the project:

```powershell
dotnet user-secrets set "AzureAd:TenantId" "YOUR_TENANT_GUID"
dotnet user-secrets set "AzureAd:ClientId" "YOUR_APPLICATION_GUID"
dotnet user-secrets set "AzureAd:ClientSecret" "YOUR_SECRET_VALUE"
dotnet run --launch-profile https
```

User Secrets are loaded in Development and stored outside the repository, but are not encrypted. Environment variables `AzureAd__TenantId`, `AzureAd__ClientId`, and `AzureAd__ClientSecret` are alternatives. Restart after configuration changes. The configuration indicator checks presence and GUID format only; successful sign-in verifies the credentials.

## Manual authentication test

Follow the **User approval simulation** steps below. The bootstrap administrator lands at `/admin`, approved Staff at `/protected`, and new users at `/pending`.

Authentication links perform full navigations so Microsoft Identity UI can update cookies. `/signin-oidc`, `/signout-callback-oidc`, and `/signout-oidc` remain authentication middleware endpoints. Sign out uses the existing Microsoft flow and returns to the branded signed-out page. Entra may show its account picker and may sign you back in through organizational SSO; use a separate browser profile to test another account.

## Troubleshooting

- **Configuration required / 503:** replace all three credential placeholders and restart in Development (or supply environment variables).
- **AADSTS50011:** check the Web redirect URI and port match `https://localhost:7020/signin-oidc` exactly.
- **Invalid client secret:** use the secret Value and check its expiry.
- **Certificate warning:** run `dotnet dev-certs https --trust` and reopen the browser.
- **Consent or assignment error:** check the test application's tenant policies and assigned users with your administrator.
- **Port already in use:** stop the other process or update both launchSettings.json and the Entra redirect/logout URLs.

This is a local login diagnostic. Cookies use a dedicated name and HTTPS. It has no production deployment configuration or shared LSS dependencies.

## Verification

`dotnet build --no-restore` succeeds with zero warnings and errors. Placeholder-mode HTTP checks confirm home returns 200, protected/sign-in routes return 503, and HTTP redirects to HTTPS. Real sign-in, claims, MFA, and sign-out require a configured tenant and must be verified using the manual steps above.

## Microsoft references

- [Blazor authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)
- [Blazor with Microsoft Entra ID](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-entra?view=aspnetcore-10.0)
- [Microsoft Identity Web quickstart](https://learn.microsoft.com/en-us/entra/msidweb/getting-started/quickstart-webapp)

## SQL Server user approval

The app uses `localhost\SQLEXPRESS`, database `LSSRepo`, through Microsoft.Data.SqlClient and Windows authentication. The connection is in `database-settings.json`. An existing `ConnectionStrings:LssDatabase` configuration value (including environment variable `ConnectionStrings__LssDatabase`) overrides this local default. No SQL password is stored. TrustServerCertificate=True is for this local SQL Express setup; use a trusted SQL certificate for deployment.

Schema and first-admin bootstrap have already been completed locally. **Do not rerun either creation script.** Startup verifies the database schema version, completed bootstrap, matching Entra tenant, and exactly one approved administrator. An unavailable or inconsistent database blocks startup; there is no JSON fallback.

| Status / Role | Landing | Access |
| --- | --- | --- |
| Approved / Admin | /admin | Repository and user administration |
| Approved / Staff | /protected | Repository |
| Pending / None | /pending | Approval status and sign-out |
| Revoked | /revoked | Revocation status and sign-out |

Admin identity comes from the operator-provisioned tenant ID and user Object ID in SQL Server. Email and display name never grant access. The original bootstrap identity is historical and does not regain Admin after a transfer.

New Microsoft-authenticated users, including existing cookies first seen by this version, enter SQL as Pending/None. The old `App_Data/users.json` is retained but is no longer read or written. Its roles are not automatically imported.

### Verify locally

1. Run `dotnet run --project Lss.EntraLoginTest.csproj --launch-profile https` and open https://localhost:7020.
2. Sign in using the bootstrapped Suaid Ishak account. Open https://localhost:7020/admin; your status should be Approved / Admin.
3. In another browser profile, sign in as another account allowed by your Entra tenant. It should show Pending approval. Direct access to /protected and /admin must be denied.
4. Refresh the admin list. Choose **Approve / restore Staff** for the new user and Save.
5. In the other browser, select Check approval status. Repository access should work, but administration remains blocked.
6. Choose **Revoke access** and Save. Subsequent protected requests/actions are denied; another sign-in does not restore access. Choose **Approve / restore Staff** to restore it.
7. Restart the app to verify persistence. Sign out to verify the existing Microsoft logout flow.
8. Optional: expand **Transfer administration** for approved Staff, read the consequence, tick confirmation and submit. The successor becomes the sole Admin and you become Staff.

The registry lists provisioned users and users who have accessed the app, not every Entra directory user. Activity timestamps are UTC; recently active means activity within 15 minutes, not an exact live-session count.

### Authorization and audit

The default [Authorize] policy requires approved Staff or Admin. Administrative pages use [Authorize(Policy = "Admin")]. Status pages use the SignedIn policy. Protected interactive handlers must recheck the registry before work, as the existing test button does. Roles are read from SQL on authorization checks. Interactive server authentication is also revalidated every 30 seconds; already-displayed information cannot be removed from a browser.

Access changes recheck the actor inside a transaction, use the shared application lock, detect stale access versions, update the SQL rowversion-protected record and insert audit history atomically. Transfers demote the old Admin and promote the successor in one transaction. The only Admin cannot be revoked through the app.

For deployment, provide a restricted Windows service identity with required SELECT/INSERT/UPDATE permissions; do not run as db_owner. Restrict audit history to SELECT/INSERT and setup/lookups to SELECT. The local app currently runs as your Windows account.

### Automated checks

```powershell
dotnet build Lss.EntraLoginTest.slnx
dotnet test tests/AccessTests/AccessTests.csproj
```

Tests require local SQL Express and permission to create/drop databases. They create randomly named LssAccessTests_* databases, apply the real schema, seed synthetic identities, then remove those databases. They do not modify LSSRepo. Test authentication and email responses exist only in the test project. Checks cover approval, revocation, stale updates, admin transfer, persistence, wrong-tenant rejection, email lookalikes, protected routes and antiforgery. Real Entra sign-in/MFA remains a manual check.

### Local HTTP/HTTPS redirect mismatch (AADSTS50011)

Start this app with `dotnet run --project Lss.EntraLoginTest.csproj --launch-profile https` and open **https://localhost:7020**. Development mode explicitly binds HTTPS to 7020 and HTTP to 5245, with HTTP requests redirected to HTTPS. IDE URL overrides cannot turn the development 7020 listener into plain HTTP. If changing ports, update the development listeners in Program.cs as well as launchSettings.json and the Entra redirect registrations.

If an old Microsoft error page still shows `http://localhost:7020/signin-oidc`, close it and initiate a fresh sign-in from the HTTPS home page. Do not add that HTTP URL to Entra to work around the mismatch. The expected sign-in callback remains `https://localhost:7020/signin-oidc`.

## Daily report email simulation (admin only)

Open **Daily report email** in the admin menu, or https://localhost:7020/admin/email. The previous recipient/upload form and mailbox-connect action have been removed.

The single **Check reports and send** button checks:

`C:\Users\User\Documents\LSS\LSS-Entra-Login-Test\reportsrepo`

The service resolves this as `reportsrepo` under the application's content root. The folder has been created empty locally; add your own sample report. Report files are excluded from Git and publishing, and are not exposed as static web assets.

### Behaviour

- Missing folder: directory-not-found status; no email.
- Empty folder: no-file-found status; no email.
- Files present: select the most recently modified regular, non-hidden file in this directory only. Ties use filename order. Subdirectories and filesystem links are excluded.
- The selected file must be non-empty and no larger than 2 MB. An invalid or unreadable newest file is reported; the app does not silently substitute an older report.
- Send the file to `suaid.ishak@sae-malaysia.com`, with subject **Single Buyer LSS — Daily report**.
- For this simulation the sending mailbox is also `suaid.ishak@sae-malaysia.com`. This is fixed in GraphReportMailSender.SenderMailbox and can be replaced with a dedicated report mailbox later.
- Microsoft HTTP 202 produces an accepted-for-sending status and the selected filename. It does not prove delivery; check the recipient inbox and sender Sent Items.
- Source files are left untouched. Every completed button click can send the newest file again. Simultaneous sends in this process are blocked; there is no persistent deduplication or automatic retry.

### Configure application mail permission

The service now authenticates as the application, without a user's delegated mail token or interactive mailbox connection. In the Entra app registration:

1. Open **API permissions > Add a permission > Microsoft Graph > Application permissions**.
2. Add **Mail.Send** and have your tenant administrator **Grant admin consent**.
3. Ensure the sender has a Microsoft Graph-supported mailbox (normally Exchange Online). Have your Exchange administrator restrict application access to the intended sending mailbox as appropriate.
4. The existing AzureAd tenant ID, client ID and client credential configuration are reused. Do not commit secrets. The previous delegated Mail.Send permission alone does not authorize this flow; it can be removed if no other app feature uses it.
5. Restart with `dotnet run --project Lss.EntraLoginTest.csproj --launch-profile https`, sign in as an LSS Admin and open Daily report email.

Add a sample report to reportsrepo, click the button, and inspect the returned status. This is a real email send when configured. If sending is unconfirmed, check Sent Items before retrying to avoid a duplicate. Application mail setup does not change the app's user approval roles or Entra sign-in/logout routes.

### Future plant API / daily execution

This button simulates the trigger only. No plant API endpoint, report generator, background schedule, or once-per-day enforcement has been added. The ReportDirectory and IReportMailSender services can be reused when those modules are built. The application token flow supports unattended mail; the future API/worker will also need its own authorization, report-completion handling, and durable delivery/deduplication policy.

The button is Admin-only and POST requests require anti-forgery validation. Tests use isolated temporary report folders and simulated Microsoft responses; they never send real mail or alter files in your reportsrepo directory.

References: [Microsoft Graph sendMail](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0), [Microsoft Identity Web application token acquisition](https://github.com/azuread/microsoft-identity-web/wiki/web-apps).

## SQL Server access schema scripts

See [database/README.md](database/README.md) for the SQL Server schema and one-time administrator provisioning scripts targeting LSSRepo. The app now uses these SQL tables. The scripts are for first installation only, not for restarting an existing installation.
