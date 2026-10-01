/*
 OPTIONAL, OPERATOR-ONLY FIRST ADMIN SETUP.
 Run AFTER 001_create_access_schema.sql.
 Tenant and user object IDs are filled in using the values supplied by the operator.
 Never use the application/client ID as the user Object ID.
 This does not verify the account against Entra; the operator must verify it.
 This script refuses to re-bootstrap an existing installation.
*/
USE [LSSRepo];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @TenantId uniqueidentifier = '27971c0d-2474-4fa6-bc9c-d30415822fc9';
DECLARE @UserObjectId uniqueidentifier = '0ff6f36b-a708-4653-b78a-c926f0785a49';
DECLARE @DisplayName nvarchar(200) = N'Suaid Ishak';
DECLARE @Email nvarchar(254) = N'suaid.ishak@sae-malaysia.com';

IF @TenantId IS NULL OR @UserObjectId IS NULL
   OR @TenantId = '00000000-0000-0000-0000-000000000000'
   OR @UserObjectId = '00000000-0000-0000-0000-000000000000'
    THROW 51001, 'Set the verified Tenant ID and User Object ID before running bootstrap.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'LSS.AccessAdministration', @LockMode = 'Exclusive',
        @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @LockResult < 0 THROW 51002, 'Could not acquire the LSS setup lock.', 1;

    IF EXISTS (SELECT 1 FROM lss.SystemSetup WITH (UPDLOCK, HOLDLOCK))
       OR EXISTS (SELECT 1 FROM lss.Users WITH (UPDLOCK, HOLDLOCK))
        THROW 51003, 'Setup or users already exist. Use a reviewed migration or recovery process, not bootstrap.', 1;

    INSERT lss.SystemSetup (SetupId, TenantId) VALUES (1, @TenantId);

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();
    INSERT lss.Users
        (TenantId, EntraObjectId, DisplayName, Email, StatusCode, RoleCode,
         CreatedAtUtc, UpdatedAtUtc, AccessChangedAtUtc)
    VALUES
        (@TenantId, @UserObjectId, @DisplayName, @Email, 'Approved', 'Admin',
         @Now, @Now, @Now);
    DECLARE @AdminUserId bigint = CONVERT(bigint, SCOPE_IDENTITY());

    INSERT lss.AccessAudit
        (TargetUserId, ActorKind, ActorReference, ActionCode, NewStatusCode, NewRoleCode, Reason, ChangedAtUtc)
    VALUES
        (@AdminUserId, 'Deployment', ORIGINAL_LOGIN(), 'Bootstrapped', 'Approved', 'Admin',
         N'Initial administrator provisioned using operator-verified Entra tenant and user object IDs.', @Now);

    UPDATE lss.SystemSetup
        SET BootstrapCompleted = 1, InitialAdminUserId = @AdminUserId,
            BootstrapCompletedAtUtc = @Now, BootstrapCompletedBy = ORIGINAL_LOGIN()
        WHERE SetupId = 1;

    COMMIT TRANSACTION;
    SELECT UserId, TenantId, EntraObjectId, DisplayName, Email, StatusCode, RoleCode
        FROM lss.Users WHERE UserId = @AdminUserId;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

