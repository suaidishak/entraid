using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace AccessTests;

public sealed class TestDatabase : IDisposable
{
    private readonly string name = "LssAccessTests_" + Guid.NewGuid().ToString("N");
    private const string Master = @"Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
    private bool disposed;
    public string ConnectionString => new SqlConnectionStringBuilder(Master) { InitialCatalog = name }.ConnectionString;
    public IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["AzureAd:TenantId"] = TestIdentity.Tenant, ["ConnectionStrings:LssDatabase"] = ConnectionString
    }).Build();

    public TestDatabase()
    {
        Execute(Master, $"CREATE DATABASE [{name}]");
        try
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            foreach (var file in new[] { "database/001_create_access_schema.sql", "database/003_create_repository_schema.sql" })
            {
                var schema = File.ReadAllText(Path.Combine(root, file))
                    .Replace("USE [LSSRepo];", $"USE [{name}];");
                foreach (var batch in Regex.Split(schema, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(batch)) new SqlCommand(batch, connection).ExecuteNonQuery();
            }
            new SqlCommand("""
                INSERT lss.SystemSetup (SetupId,TenantId) VALUES (1,'11111111-1111-1111-1111-111111111111');
                INSERT lss.Users (TenantId,EntraObjectId,DisplayName,Email,StatusCode,RoleCode)
                VALUES ('11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222','Test admin','admin@example.com','Approved','Admin');
                UPDATE lss.SystemSetup SET BootstrapCompleted=1,InitialAdminUserId=1,
                    BootstrapCompletedAtUtc=SYSUTCDATETIME(),BootstrapCompletedBy='Isolated test' WHERE SetupId=1;
                """, connection).ExecuteNonQuery();
        }
        catch { Dispose(); throw; }
    }
    public int Scalar(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return (int)command.ExecuteScalar()!;
    }

    public IReadOnlyList<string> Strings(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.IsDBNull(0) ? "" : reader.GetValue(0).ToString()!);
        return values;
    }
    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
    public void Dispose()
    {
        if (disposed) return;
        if (!Regex.IsMatch(name, "^LssAccessTests_[a-f0-9]{32}$")) throw new InvalidOperationException();
        SqlConnection.ClearAllPools();
        Execute(Master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        disposed = true;
    }
}
