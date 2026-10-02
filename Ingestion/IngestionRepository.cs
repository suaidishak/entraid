using System.Data;
using Microsoft.Data.SqlClient;

namespace Lss.EntraLoginTest.Ingestion;

public sealed record IngestionRunSummary(
    long RunId, string Trigger, string Status, string? RequestedBy,
    DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    int Attempted, int Succeeded, int Failed, int ReadingsInserted, int AnomaliesInserted, string? Message);

public sealed record AnomalySummary(
    long AnomalyId, string SiteCode, string FeedCode, string AnomalyCode, string? Field,
    int? IntervalNumber, string Detail, DateTime DetectedAtUtc);

public sealed record IngestionTotals(int Sites, int Runs, int ForecastReadings, int MmfReadings, int Anomalies);

/// <summary>SQL storage for the collection module (schema `repo`).</summary>
public sealed class IngestionRepository(IConfiguration configuration)
{
    private readonly string connectionString = configuration.GetConnectionString("LssDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:LssDatabase is required.");

    private SqlConnection Open()
    {
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    public long BeginRun(string trigger, string? requestedBy)
    {
        using var connection = Open();
        using var command = Command(connection, """
            INSERT repo.IngestionRuns (TriggerCode, StatusCode, RequestedBy)
            OUTPUT INSERTED.RunId
            VALUES (@trigger, 'Running', @requestedBy);
            """, ("@trigger", trigger), ("@requestedBy", requestedBy));
        return (long)command.ExecuteScalar()!;
    }

    public void CompleteRun(long runId, string status, int attempted, int succeeded, int failed,
        int readingsInserted, int anomaliesInserted, string? message)
    {
        using var connection = Open();
        using var command = Command(connection, """
            UPDATE repo.IngestionRuns SET StatusCode=@status, CompletedAtUtc=SYSUTCDATETIME(),
                EndpointsAttempted=@attempted, EndpointsSucceeded=@succeeded, EndpointsFailed=@failed,
                ReadingsInserted=@readings, AnomaliesInserted=@anomalies, Message=@message
            WHERE RunId=@run;
            """, ("@status", status), ("@attempted", attempted), ("@succeeded", succeeded),
            ("@failed", failed), ("@readings", readingsInserted), ("@anomalies", anomaliesInserted),
            ("@message", message), ("@run", runId));
        command.ExecuteNonQuery();
    }

    public int EnsureSite(string siteCode, string? plantName)
    {
        using var connection = Open();
        using var command = Command(connection, """
            IF NOT EXISTS (SELECT 1 FROM repo.Sites WHERE SiteCode=@code)
                INSERT repo.Sites (SiteCode, PlantName) VALUES (@code, @name);
            ELSE IF @name IS NOT NULL
                UPDATE repo.Sites SET PlantName=@name WHERE SiteCode=@code AND (PlantName IS NULL OR PlantName<>@name);
            SELECT SiteId FROM repo.Sites WHERE SiteCode=@code;
            """, ("@code", siteCode), ("@name", plantName));
        return (int)command.ExecuteScalar()!;
    }

    public void EnsureEndpoint(int siteId, FeedKind feed, string url)
    {
        using var connection = Open();
        using var command = Command(connection, """
            IF NOT EXISTS (SELECT 1 FROM repo.ApiEndpoints WHERE SiteId=@site AND FeedCode=@feed)
                INSERT repo.ApiEndpoints (SiteId, FeedCode, Url) VALUES (@site, @feed, @url);
            ELSE
                UPDATE repo.ApiEndpoints SET Url=@url, UpdatedAtUtc=SYSUTCDATETIME()
                WHERE SiteId=@site AND FeedCode=@feed AND Url<>@url;
            """, ("@site", siteId), ("@feed", feed.ToCode()), ("@url", url));
        command.ExecuteNonQuery();
    }

    public long InsertForecastReadings(long runId, int siteId, FeedKind feed, ForecastFeed parsed,
        string? sourceUrl, DateTime ingestedAtUtc)
    {
        if (parsed.Intervals.Count == 0) return 0;
        var table = new DataTable();
        table.Columns.Add("RunId", typeof(long));
        table.Columns.Add("SiteId", typeof(int));
        table.Columns.Add("FeedCode", typeof(string));
        table.Columns.Add("IntervalNumber", typeof(int));
        table.Columns.Add("IntervalStartTimeLocal", typeof(DateTime));
        table.Columns.Add("IntervalEndTimeLocal", typeof(DateTime));
        table.Columns.Add("IntervalLengthMinutes", typeof(int));
        table.Columns.Add("ResultParameter", typeof(string));
        table.Columns.Add("ForecastValue", typeof(decimal));
        table.Columns.Add("ValueUnit", typeof(string));
        table.Columns.Add("SourceUrl", typeof(string));
        table.Columns.Add("IngestedAtUtc", typeof(DateTime));

        foreach (var interval in parsed.Intervals)
        {
            var row = table.NewRow();
            row["RunId"] = runId;
            row["SiteId"] = siteId;
            row["FeedCode"] = feed.ToCode();
            row["IntervalNumber"] = interval.Number;
            row["IntervalStartTimeLocal"] = (object?)interval.StartLocal ?? DBNull.Value;
            row["IntervalEndTimeLocal"] = (object?)interval.EndLocal ?? DBNull.Value;
            row["IntervalLengthMinutes"] = (object?)interval.LengthMinutes ?? DBNull.Value;
            row["ResultParameter"] = (object?)interval.Parameter ?? DBNull.Value;
            row["ForecastValue"] = (object?)interval.Value ?? DBNull.Value;
            row["ValueUnit"] = (object?)interval.Unit ?? DBNull.Value;
            row["SourceUrl"] = (object?)sourceUrl ?? DBNull.Value;
            row["IngestedAtUtc"] = ingestedAtUtc;
            table.Rows.Add(row);
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var bulk = Bulk(connection, transaction, "repo.ForecastReadings", table))
            bulk.WriteToServer(table);
        transaction.Commit();
        return table.Rows.Count;
    }

    public long InsertMmfReadings(long runId, int siteId, MmfFeed parsed, string? sourceUrl, DateTime ingestedAtUtc)
    {
        if (parsed.Readings.Count == 0) return 0;
        var table = new DataTable();
        table.Columns.Add("RunId", typeof(long));
        table.Columns.Add("SiteId", typeof(int));
        table.Columns.Add("ParameterName", typeof(string));
        table.Columns.Add("ReadingTimeLocal", typeof(DateTime));
        table.Columns.Add("Unit", typeof(string));
        table.Columns.Add("SeriesIndex", typeof(int));
        table.Columns.Add("ReadingValue", typeof(decimal));
        table.Columns.Add("SourceUrl", typeof(string));
        table.Columns.Add("IngestedAtUtc", typeof(DateTime));

        foreach (var reading in parsed.Readings)
        {
            var row = table.NewRow();
            row["RunId"] = runId;
            row["SiteId"] = siteId;
            row["ParameterName"] = reading.Parameter;
            row["ReadingTimeLocal"] = (object?)reading.TimeLocal ?? DBNull.Value;
            row["Unit"] = (object?)reading.Unit ?? DBNull.Value;
            row["SeriesIndex"] = reading.SeriesIndex;
            row["ReadingValue"] = (object?)reading.Value ?? DBNull.Value;
            row["SourceUrl"] = (object?)sourceUrl ?? DBNull.Value;
            row["IngestedAtUtc"] = ingestedAtUtc;
            table.Rows.Add(row);
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var bulk = Bulk(connection, transaction, "repo.MmfReadings", table))
            bulk.WriteToServer(table);
        transaction.Commit();
        return table.Rows.Count;
    }

    public long InsertAnomalies(long runId, int siteId, FeedKind feed, IReadOnlyList<DataAnomaly> anomalies)
    {
        if (anomalies.Count == 0) return 0;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var anomaly in anomalies)
        {
            using var command = Command(connection, transaction, """
                INSERT repo.DataAnomalies (RunId, SiteId, FeedCode, AnomalyCode, FieldName, IntervalNumber, IntervalStartTimeLocal, Detail)
                VALUES (@run, @site, @feed, @code, @field, @interval, @start, @detail);
                """, ("@run", runId), ("@site", siteId), ("@feed", feed.ToCode()), ("@code", anomaly.Code),
                ("@field", anomaly.Field), ("@interval", anomaly.IntervalNumber),
                ("@start", anomaly.IntervalStartTimeLocal), ("@detail", anomaly.Detail));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return anomalies.Count;
    }

    public IReadOnlyList<IngestionRunSummary> RecentRuns(int take)
    {
        using var connection = Open();
        using var command = Command(connection, """
            SELECT TOP (@take) RunId, TriggerCode, StatusCode, RequestedBy, StartedAtUtc, CompletedAtUtc,
                   EndpointsAttempted, EndpointsSucceeded, EndpointsFailed, ReadingsInserted, AnomaliesInserted, Message
            FROM repo.IngestionRuns ORDER BY RunId DESC;
            """, ("@take", take));
        var runs = new List<IngestionRunSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            runs.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTime(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9),
                reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetString(11)));
        return runs;
    }

    public IReadOnlyList<AnomalySummary> RecentAnomalies(int take)
    {
        using var connection = Open();
        using var command = Command(connection, """
            SELECT TOP (@take) a.AnomalyId, s.SiteCode, a.FeedCode, a.AnomalyCode, a.FieldName,
                   a.IntervalNumber, a.Detail, a.DetectedAtUtc
            FROM repo.DataAnomalies a
            JOIN repo.Sites s ON s.SiteId = a.SiteId
            ORDER BY a.AnomalyId DESC;
            """, ("@take", take));
        var anomalies = new List<AnomalySummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            anomalies.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetString(6), reader.GetDateTime(7)));
        return anomalies;
    }

    public IngestionTotals Totals()
    {
        using var connection = Open();
        using var command = Command(connection, """
            SELECT (SELECT COUNT(*) FROM repo.Sites),
                   (SELECT COUNT(*) FROM repo.IngestionRuns),
                   (SELECT COUNT(*) FROM repo.ForecastReadings),
                   (SELECT COUNT(*) FROM repo.MmfReadings),
                   (SELECT COUNT(*) FROM repo.DataAnomalies);
            """);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
    }

    private static SqlBulkCopy Bulk(SqlConnection connection, SqlTransaction transaction, string destination, DataTable table)
    {
        var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = destination,
            BatchSize = 5000
        };
        foreach (DataColumn column in table.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        return bulk;
    }

    private static SqlCommand Command(SqlConnection connection, string sql,
        params (string Name, object? Value)[] parameters) =>
        Command(connection, null, sql, parameters);

    private static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
