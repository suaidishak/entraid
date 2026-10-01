/*
 LSS user access schema v1 — SQL Server 2022 / SQL Express.
 Run in SSMS against localhost\SQLEXPRESS, database LSSRepo.
 This script creates schema objects only. It does not seed a person, migrate JSON,
 create logins, grant permissions, or change the application's storage provider.

 All objects are created in a transaction. If one of the five target tables already
 exists, the entire script stops; this is intentionally not a silent schema upgrade.
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

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'lss.Roles') IS NOT NULL
       OR OBJECT_ID(N'lss.AccessStatuses') IS NOT NULL
       OR OBJECT_ID(N'lss.SystemSetup') IS NOT NULL
       OR OBJECT_ID(N'lss.Users') IS NOT NULL
       OR OBJECT_ID(N'lss.AccessAudit') IS NOT NULL
        THROW 51000, 'An LSS target object already exists. No changes were made. Use a reviewed migration for an existing schema.', 1;

    IF SCHEMA_ID(N'lss') IS NULL
        EXEC(N'CREATE SCHEMA [lss] AUTHORIZATION [dbo];');

    CREATE TABLE lss.Roles
    (
        RoleCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(50) NOT NULL,
        CONSTRAINT PK_LssRoles PRIMARY KEY (RoleCode),
        CONSTRAINT CK_LssRoles_Code CHECK (RoleCode IN ('None', 'Staff', 'Admin'))
    );

    CREATE TABLE lss.AccessStatuses
    (
        StatusCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(50) NOT NULL,
        CONSTRAINT PK_LssAccessStatuses PRIMARY KEY (StatusCode),
        CONSTRAINT CK_LssAccessStatuses_Code CHECK (StatusCode IN ('Pending', 'Approved', 'Revoked'))
    );

    INSERT lss.Roles (RoleCode, DisplayName)
        VALUES ('None', N'No repository role'), ('Staff', N'Staff'), ('Admin', N'Administrator');
    INSERT lss.AccessStatuses (StatusCode, DisplayName)
        VALUES ('Pending', N'Pending approval'), ('Approved', N'Approved'), ('Revoked', N'Access revoked');

    -- One row represents this single-tenant installation.
    -- It is inserted by 002_bootstrap_admin.sql, not by ordinary sign-in.
    CREATE TABLE lss.SystemSetup
    (
        SetupId tinyint NOT NULL CONSTRAINT DF_LssSetup_Id DEFAULT (1),
        TenantId uniqueidentifier NOT NULL,
        SchemaVersion int NOT NULL CONSTRAINT DF_LssSetup_Version DEFAULT (1),
        BootstrapCompleted bit NOT NULL CONSTRAINT DF_LssSetup_Completed DEFAULT (0),
        InitialAdminUserId bigint NULL,
        BootstrapCompletedAtUtc datetime2(3) NULL,
        BootstrapCompletedBy nvarchar(128) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LssSetup_Created DEFAULT (SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_LssSystemSetup PRIMARY KEY (SetupId),
        CONSTRAINT UQ_LssSystemSetup_Tenant UNIQUE (TenantId),
        CONSTRAINT CK_LssSetup_Singleton CHECK (SetupId = 1),
        CONSTRAINT CK_LssSetup_Tenant CHECK (TenantId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_LssSetup_Version CHECK (SchemaVersion >= 1),
        CONSTRAINT CK_LssSetup_Completed CHECK
        (
            (BootstrapCompleted = 0 AND InitialAdminUserId IS NULL
                AND BootstrapCompletedAtUtc IS NULL AND BootstrapCompletedBy IS NULL)
            OR
            (BootstrapCompleted = 1 AND InitialAdminUserId IS NOT NULL
                AND BootstrapCompletedAtUtc IS NOT NULL AND BootstrapCompletedBy IS NOT NULL
                AND LEN(LTRIM(RTRIM(BootstrapCompletedBy))) > 0)
        )
    );

    CREATE TABLE lss.Users
    (
        UserId bigint IDENTITY(1,1) NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        EntraObjectId uniqueidentifier NOT NULL,
        DisplayName nvarchar(200) NULL,
        Email nvarchar(254) NULL, -- Display/contact information only; NOT an identity key.
        StatusCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_LssUsers_Status DEFAULT ('Pending'),
        RoleCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_LssUsers_Role DEFAULT ('None'),
        AuthorizationVersion bigint NOT NULL CONSTRAINT DF_LssUsers_AuthVersion DEFAULT (1),
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LssUsers_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LssUsers_Updated DEFAULT (SYSUTCDATETIME()),
        FirstSignInAtUtc datetime2(3) NULL, -- Null for an administrator provisioned before first sign-in.
        LastSignInAtUtc datetime2(3) NULL,
        LastActivityAtUtc datetime2(3) NULL,
        AccessChangedAtUtc datetime2(3) NULL,
        AccessChangedByUserId bigint NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_LssUsers PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT UQ_LssUsers_EntraIdentity UNIQUE (TenantId, EntraObjectId),
        CONSTRAINT FK_LssUsers_Tenant FOREIGN KEY (TenantId)
            REFERENCES lss.SystemSetup (TenantId),
        CONSTRAINT FK_LssUsers_Status FOREIGN KEY (StatusCode)
            REFERENCES lss.AccessStatuses (StatusCode),
        CONSTRAINT FK_LssUsers_Role FOREIGN KEY (RoleCode)
            REFERENCES lss.Roles (RoleCode),
        CONSTRAINT FK_LssUsers_AccessChangedBy FOREIGN KEY (AccessChangedByUserId)
            REFERENCES lss.Users (UserId),
        CONSTRAINT CK_LssUsers_ObjectId CHECK (EntraObjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_LssUsers_AuthVersion CHECK (AuthorizationVersion >= 1),
        CONSTRAINT CK_LssUsers_AccessState CHECK
        (
            (StatusCode = 'Pending' AND RoleCode = 'None')
            OR (StatusCode = 'Approved' AND RoleCode IN ('Staff', 'Admin'))
            OR (StatusCode = 'Revoked' AND RoleCode IN ('None', 'Staff', 'Admin'))
        ),
        CONSTRAINT CK_LssUsers_Name CHECK (DisplayName IS NULL OR LEN(LTRIM(RTRIM(DisplayName))) > 0),
        CONSTRAINT CK_LssUsers_Email CHECK (Email IS NULL OR LEN(LTRIM(RTRIM(Email))) > 0),
        CONSTRAINT CK_LssUsers_SignInTimes CHECK
        (
            (FirstSignInAtUtc IS NULL AND LastSignInAtUtc IS NULL)
            OR (FirstSignInAtUtc IS NOT NULL AND LastSignInAtUtc IS NOT NULL
                AND LastSignInAtUtc >= FirstSignInAtUtc)
        ),
        CONSTRAINT CK_LssUsers_Updated CHECK (UpdatedAtUtc >= CreatedAtUtc),
        CONSTRAINT CK_LssUsers_AccessChange CHECK
            (AccessChangedByUserId IS NULL OR AccessChangedAtUtc IS NOT NULL)
    );

    -- At MOST one approved Admin. Revoked historical Admin roles may remain.
    -- Preventing ZERO approved Admins requires the transactional application workflow;
    -- see README.md. SQL Server does not defer this index until transaction commit.
    CREATE UNIQUE INDEX UX_LssUsers_OneApprovedAdmin
        ON lss.Users (RoleCode)
        WHERE StatusCode = 'Approved' AND RoleCode = 'Admin';

    CREATE INDEX IX_LssUsers_StatusRole
        ON lss.Users (StatusCode, RoleCode, UserId)
        INCLUDE (DisplayName, Email, LastSignInAtUtc, LastActivityAtUtc);

    CREATE INDEX IX_LssUsers_AccessChangedBy
        ON lss.Users (AccessChangedByUserId)
        WHERE AccessChangedByUserId IS NOT NULL;

    ALTER TABLE lss.SystemSetup ADD CONSTRAINT FK_LssSetup_InitialAdmin
        FOREIGN KEY (InitialAdminUserId) REFERENCES lss.Users (UserId);
    -- InitialAdminUserId is historical provenance, not a permanent authorization grant.

    CREATE TABLE lss.AccessAudit
    (
        AuditId bigint IDENTITY(1,1) NOT NULL,
        TargetUserId bigint NOT NULL,
        ActorUserId bigint NULL, -- Null only for provisioning/recovery/automatic registration.
        ActorKind varchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorReference nvarchar(256) NOT NULL, -- e.g. deployment SQL login or application identity.
        ActionCode varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PreviousStatusCode varchar(10) COLLATE Latin1_General_100_BIN2 NULL,
        NewStatusCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PreviousRoleCode varchar(10) COLLATE Latin1_General_100_BIN2 NULL,
        NewRoleCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Reason nvarchar(1000) NOT NULL,
        ChangedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LssAudit_Changed DEFAULT (SYSUTCDATETIME()),
        CorrelationId uniqueidentifier NOT NULL CONSTRAINT DF_LssAudit_Correlation DEFAULT (NEWID()),
        CONSTRAINT PK_LssAccessAudit PRIMARY KEY CLUSTERED (AuditId),
        CONSTRAINT FK_LssAudit_Target FOREIGN KEY (TargetUserId) REFERENCES lss.Users (UserId),
        CONSTRAINT FK_LssAudit_Actor FOREIGN KEY (ActorUserId) REFERENCES lss.Users (UserId),
        CONSTRAINT FK_LssAudit_PreviousStatus FOREIGN KEY (PreviousStatusCode) REFERENCES lss.AccessStatuses (StatusCode),
        CONSTRAINT FK_LssAudit_NewStatus FOREIGN KEY (NewStatusCode) REFERENCES lss.AccessStatuses (StatusCode),
        CONSTRAINT FK_LssAudit_PreviousRole FOREIGN KEY (PreviousRoleCode) REFERENCES lss.Roles (RoleCode),
        CONSTRAINT FK_LssAudit_NewRole FOREIGN KEY (NewRoleCode) REFERENCES lss.Roles (RoleCode),
        CONSTRAINT CK_LssAudit_Actor CHECK
        (
            (ActorKind = 'User' AND ActorUserId IS NOT NULL)
            OR (ActorKind IN ('Deployment', 'System') AND ActorUserId IS NULL)
        ),
        CONSTRAINT CK_LssAudit_Action CHECK
            (ActionCode IN ('Registered', 'Bootstrapped', 'Approved', 'Revoked', 'Restored', 'RoleChanged', 'AdminTransferred', 'Recovered')),
        CONSTRAINT CK_LssAudit_PreviousState CHECK
        (
            (PreviousStatusCode IS NULL AND PreviousRoleCode IS NULL)
            OR (PreviousStatusCode IS NOT NULL AND PreviousRoleCode IS NOT NULL AND
                ((PreviousStatusCode = 'Pending' AND PreviousRoleCode = 'None')
                 OR (PreviousStatusCode = 'Approved' AND PreviousRoleCode IN ('Staff', 'Admin'))
                 OR (PreviousStatusCode = 'Revoked' AND PreviousRoleCode IN ('None', 'Staff', 'Admin'))))
        ),
        CONSTRAINT CK_LssAudit_NewState CHECK
        (
            (NewStatusCode = 'Pending' AND NewRoleCode = 'None')
            OR (NewStatusCode = 'Approved' AND NewRoleCode IN ('Staff', 'Admin'))
            OR (NewStatusCode = 'Revoked' AND NewRoleCode IN ('None', 'Staff', 'Admin'))
        ),
        CONSTRAINT CK_LssAudit_Reason CHECK (LEN(LTRIM(RTRIM(Reason))) > 0),
        CONSTRAINT CK_LssAudit_Reference CHECK (LEN(LTRIM(RTRIM(ActorReference))) > 0)
    );

    CREATE INDEX IX_LssAccessAudit_TargetTime
        ON lss.AccessAudit (TargetUserId, ChangedAtUtc DESC, AuditId DESC);
    CREATE INDEX IX_LssAccessAudit_ActorTime
        ON lss.AccessAudit (ActorUserId, ChangedAtUtc DESC)
        WHERE ActorUserId IS NOT NULL;
    CREATE INDEX IX_LssAccessAudit_Correlation
        ON lss.AccessAudit (CorrelationId);

    COMMIT TRANSACTION;
    PRINT 'LSS access schema created. No administrator has been provisioned.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

