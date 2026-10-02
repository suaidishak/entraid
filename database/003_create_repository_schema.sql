/*
  LSS repository ingestion schema v1 — SQL Server 2022 / SQL Express.
  Run in SSMS (or sqlcmd) against localhost\SQLEXPRESS, database LSSRepo.

  Creates the `repo` schema used by the API collection / ingestion module:
    repo.Sites             — LSS plants
    repo.ApiEndpoints      — per-site feed endpoint registry
    repo.IngestionRuns     — one row per collection run (manual or automatic)
    repo.ForecastReadings  — DDQ (day/week/four-month/rolling-24) intervals
    repo.MmfReadings       — meteorological parameter readings
    repo.DataAnomalies     — detected anomalies (null/negative/out-of-range/etc.)

  This script creates schema objects only. All objects are created in a
  transaction; if any target object already exists the whole script stops.
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

    IF OBJECT_ID(N'repo.Sites') IS NOT NULL
       OR OBJECT_ID(N'repo.ApiEndpoints') IS NOT NULL
       OR OBJECT_ID(N'repo.IngestionRuns') IS NOT NULL
       OR OBJECT_ID(N'repo.ForecastReadings') IS NOT NULL
       OR OBJECT_ID(N'repo.MmfReadings') IS NOT NULL
       OR OBJECT_ID(N'repo.DataAnomalies') IS NOT NULL
        THROW 51100, 'A repository ingestion object already exists. No changes were made. Use a reviewed migration for an existing schema.', 1;

    IF SCHEMA_ID(N'repo') IS NULL
        EXEC(N'CREATE SCHEMA [repo] AUTHORIZATION [dbo];');

    CREATE TABLE repo.Sites
    (
        SiteId int IDENTITY(1,1) NOT NULL,
        SiteCode varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PlantName nvarchar(200) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_RepoSites_Active DEFAULT (1),
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoSites_Created DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RepoSites PRIMARY KEY CLUSTERED (SiteId),
        CONSTRAINT UQ_RepoSites_Code UNIQUE (SiteCode),
        CONSTRAINT CK_RepoSites_Code CHECK (LEN(LTRIM(RTRIM(SiteCode))) > 0)
    );

    CREATE TABLE repo.ApiEndpoints
    (
        EndpointId int IDENTITY(1,1) NOT NULL,
        SiteId int NOT NULL,
        FeedCode varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Url nvarchar(500) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_RepoEndpoints_Active DEFAULT (1),
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoEndpoints_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoEndpoints_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RepoApiEndpoints PRIMARY KEY CLUSTERED (EndpointId),
        CONSTRAINT UQ_RepoApiEndpoints_Site_Feed UNIQUE (SiteId, FeedCode),
        CONSTRAINT FK_RepoApiEndpoints_Site FOREIGN KEY (SiteId) REFERENCES repo.Sites (SiteId),
        CONSTRAINT CK_RepoApiEndpoints_Feed CHECK
            (FeedCode IN ('Rolling24', 'DayAhead', 'WeekAhead', 'FourMonthsAhead', 'Mmf')),
        CONSTRAINT CK_RepoApiEndpoints_Url CHECK (LEN(LTRIM(RTRIM(Url))) > 0)
    );

    CREATE TABLE repo.IngestionRuns
    (
        RunId bigint IDENTITY(1,1) NOT NULL,
        TriggerCode varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StatusCode varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestedBy nvarchar(256) NULL,
        StartedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoRuns_Started DEFAULT (SYSUTCDATETIME()),
        CompletedAtUtc datetime2(3) NULL,
        EndpointsAttempted int NOT NULL CONSTRAINT DF_RepoRuns_Attempted DEFAULT (0),
        EndpointsSucceeded int NOT NULL CONSTRAINT DF_RepoRuns_Succeeded DEFAULT (0),
        EndpointsFailed int NOT NULL CONSTRAINT DF_RepoRuns_Failed DEFAULT (0),
        ReadingsInserted int NOT NULL CONSTRAINT DF_RepoRuns_Readings DEFAULT (0),
        AnomaliesInserted int NOT NULL CONSTRAINT DF_RepoRuns_Anomalies DEFAULT (0),
        Message nvarchar(1000) NULL,
        CONSTRAINT PK_RepoIngestionRuns PRIMARY KEY CLUSTERED (RunId),
        CONSTRAINT CK_RepoRuns_Trigger CHECK (TriggerCode IN ('Automatic', 'Manual')),
        CONSTRAINT CK_RepoRuns_Status CHECK
            (StatusCode IN ('Running', 'Completed', 'CompletedWithAnomalies', 'Failed')),
        CONSTRAINT CK_RepoRuns_Completed CHECK
            (CompletedAtUtc IS NULL OR CompletedAtUtc >= StartedAtUtc)
    );

    CREATE TABLE repo.ForecastReadings
    (
        ReadingId bigint IDENTITY(1,1) NOT NULL,
        RunId bigint NOT NULL,
        SiteId int NOT NULL,
        FeedCode varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IntervalNumber int NOT NULL,
        IntervalStartTimeLocal datetime2(0) NULL,
        IntervalEndTimeLocal datetime2(0) NULL,
        IntervalLengthMinutes int NULL,
        ResultParameter nvarchar(50) NULL,
        ForecastValue decimal(18,6) NULL,
        ValueUnit nvarchar(10) NULL,
        SourceUrl nvarchar(500) NULL,
        IngestedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoForecast_Ingested DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RepoForecastReadings PRIMARY KEY CLUSTERED (ReadingId),
        CONSTRAINT FK_RepoForecast_Run FOREIGN KEY (RunId) REFERENCES repo.IngestionRuns (RunId),
        CONSTRAINT FK_RepoForecast_Site FOREIGN KEY (SiteId) REFERENCES repo.Sites (SiteId),
        CONSTRAINT CK_RepoForecast_Feed CHECK
            (FeedCode IN ('Rolling24', 'DayAhead', 'WeekAhead', 'FourMonthsAhead')),
        CONSTRAINT CK_RepoForecast_Interval CHECK (IntervalNumber > 0)
    );

    CREATE INDEX IX_RepoForecast_Site_Feed_Time
        ON repo.ForecastReadings (SiteId, FeedCode, IntervalStartTimeLocal)
        INCLUDE (ForecastValue, ValueUnit, RunId);

    CREATE TABLE repo.MmfReadings
    (
        ReadingId bigint IDENTITY(1,1) NOT NULL,
        RunId bigint NOT NULL,
        SiteId int NOT NULL,
        ParameterName nvarchar(100) NOT NULL,
        ReadingTimeLocal datetime2(0) NULL,
        Unit nvarchar(20) NULL,
        SeriesIndex int NOT NULL,
        ReadingValue decimal(18,6) NULL,
        SourceUrl nvarchar(500) NULL,
        IngestedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoMmf_Ingested DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RepoMmfReadings PRIMARY KEY CLUSTERED (ReadingId),
        CONSTRAINT FK_RepoMmf_Run FOREIGN KEY (RunId) REFERENCES repo.IngestionRuns (RunId),
        CONSTRAINT FK_RepoMmf_Site FOREIGN KEY (SiteId) REFERENCES repo.Sites (SiteId),
        CONSTRAINT CK_RepoMmf_Series CHECK (SeriesIndex >= 1)
    );

    CREATE INDEX IX_RepoMmf_Site_Parameter_Time
        ON repo.MmfReadings (SiteId, ParameterName, ReadingTimeLocal)
        INCLUDE (ReadingValue, Unit, SeriesIndex, RunId);

    CREATE TABLE repo.DataAnomalies
    (
        AnomalyId bigint IDENTITY(1,1) NOT NULL,
        RunId bigint NOT NULL,
        SiteId int NOT NULL,
        FeedCode varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AnomalyCode varchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FieldName nvarchar(50) NULL,
        IntervalNumber int NULL,
        IntervalStartTimeLocal datetime2(0) NULL,
        Detail nvarchar(400) NOT NULL,
        DetectedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RepoAnomalies_Detected DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RepoDataAnomalies PRIMARY KEY CLUSTERED (AnomalyId),
        CONSTRAINT FK_RepoAnomalies_Run FOREIGN KEY (RunId) REFERENCES repo.IngestionRuns (RunId),
        CONSTRAINT FK_RepoAnomalies_Site FOREIGN KEY (SiteId) REFERENCES repo.Sites (SiteId),
        CONSTRAINT CK_RepoAnomalies_Code CHECK (AnomalyCode IN
            ('EmptyFeed', 'NullTimestamp', 'MissingValue', 'NegativeValue', 'OutOfRange',
             'InvalidIntervalLength', 'AllZero', 'RequestFailed', 'ParseFailed'))
    );

    CREATE INDEX IX_RepoAnomalies_Site_Feed_Time
        ON repo.DataAnomalies (SiteId, FeedCode, DetectedAtUtc DESC, AnomalyId DESC);
    CREATE INDEX IX_RepoAnomalies_Run
        ON repo.DataAnomalies (RunId);

    COMMIT TRANSACTION;
    PRINT 'LSS repository ingestion schema created.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
