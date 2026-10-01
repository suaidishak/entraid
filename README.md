# LSS Entra Login Test

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Blazor](https://img.shields.io/badge/Blazor-Interactive%20Server-512BD4?logo=blazor&logoColor=white)
![Microsoft Entra ID](https://img.shields.io/badge/Auth-Microsoft%20Entra%20ID-0078D4?logo=microsoftazure&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-LSSRepo-CC2927?logo=microsoftsqlserver&logoColor=white)

> A standalone ASP.NET Core Blazor Web App that demonstrates **Microsoft Entra ID sign-in** backed by a **SQL Server user-approval registry**. It is a self-contained login diagnostic with no production deployment configuration and no shared LSS dependencies.

Authentication uses the OpenID Connect authorization-code flow with a server-managed authentication cookie. User approval and roles live in SQL Server (database `LSSRepo`); Microsoft Entra handles sign-in only. An optional **email module** sends report files through Microsoft Graph with the application `Mail.Send` permission.

## Contents

- [Overview](#overview)
- [Features](#features)
- [Tech stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Getting started (placeholder mode)](#getting-started-placeholder-mode)
- [Register an Entra application](#register-an-entra-application)
- [Configure credentials](#configure-credentials)
- [Roles and access](#roles-and-access)
- [SQL Server setup](#sql-server-setup)
- [Daily report email](#daily-report-email)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Project structure](#project-structure)
- [References](#references)

## Overview

The app separates **identity** (Microsoft Entra ID) from **access** (SQL Server). Entra proves who the user is; the SQL registry decides what they may do. Until an administrator approves a signed-in user, they only see a pending-approval page.

To keep the project safe to clone and run, `appsettings.json` ships with placeholder credentials. In that state the landing page explains configuration and sign-in / protected routes return **HTTP 503** — no Entra contact is attempted.

## Features

- **Entra ID sign-in** via Microsoft.Identity.Web (OpenID Connect, authorization-code flow).
- **Approval workflow**: new users enter as `Pending`, admins approve or revoke.
- **Role-based access**: `Admin`, `Staff`, `Pending`, `Revoked`, each with a dedicated landing page.
- **Admin transfer**: promote an approved Staff member to sole Admin in a single audited transaction.
- **Audit trail**: every access change is recorded in `lss.AccessAudit` with old/new state.
- **SQL-backed registry** with optimistic concurrency (`rowversion`) and a single-admin invariant.
- **Daily report email** (admin-only): sends the newest file from `reportsrepo` via Microsoft Graph.
- **Integration tests** covering approval, revocation, stale updates, admin transfer, persistence, and route/antiforgery behavior.

## Tech stack

| Area | Choice |
| --- | --- |
| Runtime | .NET 10 (`net10.0`) |
| UI | Blazor Web App, Interactive Server rendering |
| AuthN | Microsoft.Identity.Web / Microsoft.Identity.Web.UI |
| AuthZ | ASP.NET Core authorization policies + SQL-backed registry handler |
| Data | Microsoft.Data.SqlClient → SQL Server (`localhost\SQLEXPRESS`, `LSSRepo`) |
| Mail | Microsoft Graph application token (`Mail.Send`) |
| Tests | xUnit, Moq, `Microsoft.AspNetCore.Mvc.Testing` |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server Express with a `LSSRepo` database (for the full approval workflow)
- A Microsoft Entra tenant where you can register an application
- (Optional) A Microsoft 365 mailbox for the email module

## Getting started (placeholder mode)

Run from the project root:

```powershell
dotnet restore
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

Open <https://localhost:7020>.

With placeholders in place, the landing page shows configuration instructions, and sign-in / protected routes return HTTP 503. **No credentials are needed to build or view the setup page.** The `dev-certs` command may show an operating-system confirmation dialog.

## Register an Entra application

Use a **dedicated test registration**. This project never creates or modifies Entra resources; tenant consent and user-assignment policies still apply.

1. In the Microsoft Entra admin center, open **App registrations → New registration**. Give it a test-specific name and choose **Accounts in this organizational directory only**.
2. Add the **Web** platform (not SPA) with these redirect URIs:
   - `https://localhost:7020/signin-oidc`
   - `https://localhost:7020/signout-callback-oidc`
3. Set the front-channel logout URL to `https://localhost:7020/signout-oidc`.
4. Leave the implicit access-token and ID-token grant checkboxes **disabled** — this app uses the authorization-code flow.
5. Copy the **Directory (tenant) ID** and **Application (client) ID**.
6. Under **Certificates & secrets**, create a development client secret and copy its **Value** (not its secret ID).

Login alone requires no Graph API access. The `Mail.Send` permission is only needed for the [daily report email](#daily-report-email) module.

## Configure credentials

`appsettings.json` contains placeholders (`YOUR_TENANT_ID`, `YOUR_CLIENT_ID`, `YOUR_CLIENT_SECRET`). **Keep the secret out of that file.** A `UserSecretsId` is already configured in the project:

```powershell
dotnet user-secrets set "AzureAd:TenantId" "YOUR_TENANT_GUID"
dotnet user-secrets set "AzureAd:ClientId" "YOUR_APPLICATION_GUID"
dotnet user-secrets set "AzureAd:ClientSecret" "YOUR_SECRET_VALUE"
dotnet run --launch-profile https
```

User Secrets are loaded in Development and stored outside the repository (but are **not encrypted**). Environment variables are an alternative:

| Setting | Environment variable |
| --- | --- |
| `AzureAd:TenantId` | `AzureAd__TenantId` |
| `AzureAd:ClientId` | `AzureAd__ClientId` |
| `AzureAd:ClientSecret` | `AzureAd__ClientSecret` |

Restart after configuration changes. The startup configuration indicator checks **presence and GUID format only**; a successful sign-in is what actually verifies the credentials.

## Roles and access

| Status / Role | Landing | Access |
| --- | --- | --- |
| Approved / Admin | `/admin` | Repository and user administration |
| Approved / Staff | `/protected` | Repository |
| Pending / None | `/pending` | Approval status and sign-out |
| Revoked | `/revoked` | Revocation status and sign-out |

Admin identity comes from the operator-provisioned tenant ID and user Object ID in SQL Server. **Email and display name never grant access.** The original bootstrap identity is historical and does not regain Admin after a transfer.

New Microsoft-authenticated users — including existing cookies first seen by this version — enter the registry as `Pending` / `None`.

### Authorization and audit

- The default `[Authorize]` policy requires approved Staff or Admin.
- Administrative pages use `[Authorize(Policy = "Admin")]`; status pages use the `SignedIn` policy.
- Protected interactive handlers re-check the registry before doing work.
- Roles are read from SQL on every authorization check, and interactive server authentication is revalidated every 30 seconds.

Access changes re-check the actor inside a transaction, take the shared application lock, detect stale `AuthorizationVersion` values, update the `rowversion`-protected record, and insert audit history atomically. Transfers demote the old Admin and promote the successor in one transaction. **The only Admin cannot be revoked through the app.**

### Verify locally

1. Run `dotnet run --project Lss.EntraLoginTest.csproj --launch-profile https` and open <https://localhost:7020>.
2. Sign in with the bootstrapped admin account and open <https://localhost:7020/admin> — status should be **Approved / Admin**.
3. In another browser profile, sign in as a different tenant-allowed account. It should show **Pending approval**, and direct access to `/protected` and `/admin` must be denied.
4. In the admin list, choose **Approve / restore Staff** for the new user and save.
5. In the other browser, select **Check approval status** — repository access should work, but administration stays blocked.
6. Choose **Revoke access** and save; subsequent protected requests are denied and a new sign-in does not restore access. Use **Approve / restore Staff** to restore it.
7. Restart the app to verify persistence, then sign out to verify the existing Microsoft logout flow.
8. Optional: expand **Transfer administration** for an approved Staff member, confirm, and submit. The successor becomes sole Admin and you become Staff.

The registry lists provisioned users and users who have accessed the app — **not** every Entra directory user. Activity timestamps are UTC; "recently active" means activity within 15 minutes, not a live-session count.

Authentication links perform full navigations so Microsoft Identity UI can update cookies. `/signin-oidc`, `/signout-callback-oidc`, and `/signout-oidc` remain authentication middleware endpoints. Entra may show its account picker and may sign you back in via organizational SSO — use a separate browser profile to test another account.

## SQL Server setup

The app connects to `localhost\SQLEXPRESS`, database `LSSRepo`, using Microsoft.Data.SqlClient and Windows authentication. The connection string lives in `database-settings.json`:

```json
"ConnectionStrings": {
  "LssDatabase": "Server=localhost\\SQLEXPRESS;Database=LSSRepo;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
}
```

An existing `ConnectionStrings:LssDatabase` value (including environment variable `ConnectionStrings__LssDatabase`) overrides this local default. **No SQL password is stored.** `TrustServerCertificate=True` is for the local Express setup only; use a trusted SQL certificate for deployment.

Schema and first-admin bootstrap have already been completed locally. **Do not rerun either creation script.** Startup verifies the schema version, completed bootstrap, matching Entra tenant, and exactly one approved administrator. An unavailable or inconsistent database blocks startup — there is no JSON fallback. Schema details and one-time provisioning live in [`database/README.md`](database/README.md).

For deployment, provide a restricted Windows service identity with the minimum required `SELECT`/`INSERT`/`UPDATE` permissions; **do not run as `db_owner`**. Restrict audit history to `SELECT`/`INSERT` and setup/lookups to `SELECT`.

## Daily report email

Open **Daily report email** in the admin menu, or go to <https://localhost:7020/admin/email>.

The single **Check reports and send** button inspects the `reportsrepo` folder under the application content root (for example, `C:\Users\User\Documents\LSS\LSS-Entra-Login-Test\reportsrepo`). The folder is created empty locally; add your own sample report. Report files are excluded from Git and publishing and are not served as static assets.

### Behaviour

- **Missing folder** → directory-not-found status; no email.
- **Empty folder** → no-file-found status; no email.
- **Files present** → selects the most recently modified regular, non-hidden file in that directory (ties break by filename). Subdirectories and filesystem links are excluded.
- The selected file must be non-empty and no larger than **2 MB**; an invalid or unreadable newest file is reported rather than silently substituting an older one.
- Sends to `suaid.ishak@sae-malaysia.com` with subject **Single Buyer LSS — Daily report**.
- For this simulation the sending mailbox is also `suaid.ishak@sae-malaysia.com` (fixed in `GraphReportMailSender.SenderMailbox`; replace with a dedicated report mailbox later).
- A Microsoft HTTP **202** returns an accepted-for-sending status with the filename. This does **not** prove delivery — check the recipient inbox and sender Sent Items.
- Source files are left untouched; each completed click can resend the newest file. Simultaneous sends are blocked in-process, but there is **no persistent deduplication or automatic retry**.

### Configure the mail permission

The service authenticates as the **application** — no delegated user mail token or interactive mailbox connection.

1. In the Entra app registration: **API permissions → Add a permission → Microsoft Graph → Application permissions**.
2. Add **Mail.Send** and have a tenant administrator **Grant admin consent**.
3. Ensure the sender has a Graph-supported mailbox (normally Exchange Online). Have your Exchange administrator restrict application access to the intended mailbox.
4. The existing `AzureAd` tenant ID, client ID, and client secret are reused. **Do not commit secrets.**
5. Restart, sign in as an LSS Admin, and open **Daily report email**. Add a sample report, click the button, and inspect the status.

This is a **real email send** when configured. If sending is unconfirmed, check Sent Items before retrying to avoid duplicates.

> **Future work:** this button simulates the trigger only. No plant API endpoint, report generator, background schedule, or once-per-day enforcement exists yet. `ReportDirectory` and `IReportMailSender` can be reused when those modules are built.

## Testing

Build the solution and run the integration tests:

```powershell
dotnet build Lss.EntraLoginTest.slnx
dotnet test tests/AccessTests/AccessTests.csproj
```

Tests require local SQL Express and permission to create/drop databases. They create randomly named `LssAccessTests_*` databases, apply the real schema, seed synthetic identities, and remove those databases afterward. **They do not modify `LSSRepo`.** Test authentication and email responses exist only in the test project.

Coverage includes approval, revocation, stale updates, admin transfer, persistence, wrong-tenant rejection, email lookalikes, protected routes, and antiforgery. Real Entra sign-in and MFA remain manual checks.

A placeholder-mode smoke check expects: home returns **200**, protected / sign-in routes return **503**, and HTTP redirects to HTTPS. `dotnet build --no-restore` should succeed with zero warnings and errors.

## Troubleshooting

| Symptom | Resolution |
| --- | --- |
| Configuration required / 503 | Replace all three credential placeholders and restart in Development (or supply environment variables). |
| `AADSTS50011` redirect mismatch | Ensure the Web redirect URI and port match `https://localhost:7020/signin-oidc` exactly. |
| Invalid client secret | Use the secret **Value** (not the secret ID) and check its expiry. |
| Certificate warning | Run `dotnet dev-certs https --trust` and reopen the browser. |
| Consent or assignment error | Check the test app's tenant policies and assigned users with your administrator. |
| Port already in use | Stop the other process or update both `launchSettings.json` and the Entra redirect/logout URLs. |

### HTTP/HTTPS redirect mismatch

Start the app with `dotnet run --project Lss.EntraLoginTest.csproj --launch-profile https` and open **https://localhost:7020**. Development mode explicitly binds HTTPS to `7020` and HTTP to `5245`, redirecting HTTP to HTTPS; IDE URL overrides cannot turn the `7020` listener into plain HTTP.

If an old Microsoft error page still shows `http://localhost:7020/signin-oidc`, close it and start a fresh sign-in from the HTTPS home page. **Do not** add that HTTP URL to Entra to work around the mismatch. When changing ports, update the development listeners in `Program.cs`, `launchSettings.json`, and the Entra redirect registrations together.

## Project structure

```
Lss.EntraLoginTest.slnx           Solution
Lss.EntraLoginTest.csproj         Web project (.NET 10)
Program.cs                        Pipeline, DI, auth policies, admin endpoints
EntraSetup.cs                     Placeholder/configuration presence check
appsettings.json                  AzureAd placeholders + logging
database-settings.json            Local SQL connection string
Access/                           SQL-backed user registry, authorization handler, revalidation
Components/                       Blazor layout, routes, and pages (Admin, Protected, Pending, …)
Email/                            Report directory + Microsoft Graph mail sender
Areas/MicrosoftIdentity/          Signed-out page for Microsoft Identity UI
database/                         SQL schema + first-admin bootstrap scripts (see its README)
tests/AccessTests/                xUnit integration tests
reportsrepo/                      Drop reports here (excluded from Git/publish)
wwwroot/                          Static assets
```

## References

- [Blazor authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)
- [Blazor Web App with Microsoft Entra ID](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-entra?view=aspnetcore-10.0)
- [Microsoft Identity Web — web app quickstart](https://learn.microsoft.com/en-us/entra/msidweb/getting-started/quickstart-webapp)
- [Microsoft Graph sendMail](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0)
- [Microsoft Identity Web — application token acquisition](https://github.com/azuread/microsoft-identity-web/wiki/web-apps)

---

This is a local login diagnostic. Cookies use a dedicated name (`__Host-Lss.EntraLoginTest`) over HTTPS. It has no production deployment configuration or shared LSS dependencies.
