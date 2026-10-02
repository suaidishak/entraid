namespace Lss.EntraLoginTest.Ingestion;

/// <summary>One DDQ forecast interval, keeping both raw text and parsed values.</summary>
public sealed record ForecastInterval(
    int Number,
    string? StartText,
    DateTime? StartLocal,
    DateTime? EndLocal,
    string? LengthText,
    int? LengthMinutes,
    string? Parameter,
    string? ValueText,
    decimal? Value,
    string? Unit);

/// <summary>A parsed DDQ forecast feed.</summary>
public sealed record ForecastFeed(string? PlantName, IReadOnlyList<ForecastInterval> Intervals);

/// <summary>One MMF parameter reading for a timestamp (MMF1..MMFn map to SeriesIndex).</summary>
public sealed record MmfReading(
    string Parameter,
    string? TimeText,
    DateTime? TimeLocal,
    string? Unit,
    int SeriesIndex,
    string? ValueText,
    decimal? Value);

/// <summary>A parsed MMF feed.</summary>
public sealed record MmfFeed(string? PlantName, IReadOnlyList<MmfReading> Readings);

/// <summary>A detected data anomaly, ready to persist.</summary>
public sealed record DataAnomaly(
    string Code,
    string? Field,
    int? IntervalNumber,
    DateTime? IntervalStartTimeLocal,
    string Detail);

public static class AnomalyCodes
{
    public const string EmptyFeed = "EmptyFeed";
    public const string NullTimestamp = "NullTimestamp";
    public const string MissingValue = "MissingValue";
    public const string NegativeValue = "NegativeValue";
    public const string OutOfRange = "OutOfRange";
    public const string InvalidIntervalLength = "InvalidIntervalLength";
    public const string AllZero = "AllZero";
    public const string RequestFailed = "RequestFailed";
    public const string ParseFailed = "ParseFailed";
}

/// <summary>Summary of a completed ingestion run.</summary>
public sealed record IngestionRunResult(
    long RunId,
    string Status,
    int EndpointsAttempted,
    int EndpointsSucceeded,
    int EndpointsFailed,
    int ReadingsInserted,
    int AnomaliesInserted,
    TimeSpan Duration);
