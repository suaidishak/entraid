using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace Lss.EntraLoginTest.Access;

public enum AppRole { Pending, Staff, Admin, Revoked }
public sealed record RegisteredUser(long Id, string Key, string Name, string Email, string Status,
    string DatabaseRole, long AuthorizationVersion, byte[] RowVersion,
    DateTimeOffset? FirstSignIn, DateTimeOffset? LastSignIn, DateTimeOffset? LastActivity)
{
    public AppRole Role => Status switch
    {
        "Approved" when DatabaseRole == "Admin" => AppRole.Admin,
        "Approved" when DatabaseRole == "Staff" => AppRole.Staff,
        "Revoked" => AppRole.Revoked,
        _ => AppRole.Pending
    };
}

// Each operation owns its connection. No role cache, JSON fallback or automatic admin bootstrap.
public sealed class UserRegistry(IConfiguration configuration)
{
    private readonly string connectionString = configuration.GetConnectionString("LssDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:LssDatabase is required.");
    private readonly Guid tenantId = Guid.TryParse(configuration["AzureAd:TenantId"], out var tenant) ? tenant : Guid.Empty;
    private const string Columns = "UserId,TenantId,EntraObjectId,DisplayName,Email,StatusCode,RoleCode,AuthorizationVersion,RowVersion,FirstSignInAtUtc,LastSignInAtUtc,LastActivityAtUtc";

    private (Guid Tenant, Guid Object)? Identity(ClaimsPrincipal user)
    {
        var tid = user.FindFirstValue("tid") ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        var oid = user.FindFirstValue("oid") ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
        return user.Identity?.IsAuthenticated == true && Guid.TryParse(tid, out var t) && t == tenantId &&
            t != Guid.Empty && Guid.TryParse(oid, out var o) && o != Guid.Empty ? (t, o) : null;
    }

    private SqlConnection Open()
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            using var command = Command(connection, null, """
                SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
                SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON;
                SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
                IF NOT EXISTS (SELECT 1 FROM lss.SystemSetup WHERE SetupId=1
                    AND BootstrapCompleted=1 AND SchemaVersion=1 AND TenantId=@tenant)
                    THROW 51010, 'LSS database setup is incomplete or the configured tenant does not match.', 1;
                """, ("@tenant", tenantId));
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public void VerifySetup()
    {
        using var connection = Open();
        using var command = Command(connection, null, "SELECT COUNT(*) FROM lss.Users WHERE StatusCode='Approved' AND RoleCode='Admin'");
        if ((int)command.ExecuteScalar()! != 1) throw new InvalidOperationException("LSS requires exactly one approved administrator.");
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void Lock(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'LSS.AccessAdministration',
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51011, 'LSS access update is busy. Try again.', 1;
            """);
        command.ExecuteNonQuery();
    }

    private static RegisteredUser Read(SqlDataReader reader)
    {
        DateTimeOffset? Time(int index) => reader.IsDBNull(index) ? null :
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(index), DateTimeKind.Utc));
        return new(reader.GetInt64(0), $"{reader.GetGuid(1):D}:{reader.GetGuid(2):D}",
            reader.IsDBNull(3) ? "Microsoft user" : reader.GetString(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetInt64(7), (byte[])reader[8], Time(9), Time(10), Time(11));
    }

    private RegisteredUser? Find(SqlConnection connection, SqlTransaction? transaction, string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var t) || t != tenantId ||
            !Guid.TryParse(parts[1], out var o)) return null;
        using var command = Command(connection, transaction,
            $"SELECT {Columns} FROM lss.Users WHERE TenantId=@t AND EntraObjectId=@o",
            ("@t", t), ("@o", o));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public RegisteredUser? Current(ClaimsPrincipal principal)
    {
        var identity = Identity(principal);
        if (identity is null) return null;
        using var connection = Open();
        return Find(connection, null, $"{identity.Value.Tenant:D}:{identity.Value.Object:D}");
    }

    public void RecordActivity(ClaimsPrincipal principal, bool signIn = false)
    {
        var identity = Identity(principal);
        if (identity is null) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Lock(connection, transaction);
        var key = $"{identity.Value.Tenant:D}:{identity.Value.Object:D}";
        var existing = Find(connection, transaction, key);
        var now = DateTime.UtcNow;
        if (existing is not null && !signIn && existing.LastActivity > DateTimeOffset.UtcNow.AddMinutes(-1))
        { transaction.Commit(); return; }
        static string? Clip(string? value, int maximum) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maximum)];
        var email = Clip(principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue(ClaimTypes.Upn), 254);
        var name = Clip(principal.FindFirstValue("name") ?? principal.Identity?.Name, 200);
        if (existing is null)
        {
            using var insert = Command(connection, transaction, """
                INSERT lss.Users (TenantId,EntraObjectId,DisplayName,Email,FirstSignInAtUtc,LastSignInAtUtc,LastActivityAtUtc)
                VALUES (@t,@o,@name,@email,@now,@now,@now);
                SELECT CONVERT(bigint,SCOPE_IDENTITY());
                """, ("@t", identity.Value.Tenant), ("@o", identity.Value.Object),
                ("@name", name), ("@email", email), ("@now", now));
            var id = (long)insert.ExecuteScalar()!;
            Audit(connection, transaction, id, null, key, "Registered", null, null, "Pending", "None",
                "Registered after Microsoft authentication.", Guid.NewGuid());
        }
        else
        {
            using var update = Command(connection, transaction, """
                UPDATE lss.Users SET DisplayName=COALESCE(@name,DisplayName),Email=COALESCE(@email,Email),
                    FirstSignInAtUtc=COALESCE(FirstSignInAtUtc,@now),
                    LastSignInAtUtc=CASE WHEN @signin=1 OR LastSignInAtUtc IS NULL THEN @now ELSE LastSignInAtUtc END,
                    LastActivityAtUtc=@now,UpdatedAtUtc=@now WHERE UserId=@id;
                """, ("@name", name), ("@email", email), ("@now", now), ("@signin", signIn), ("@id", existing.Id));
            update.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<RegisteredUser> List(ClaimsPrincipal actor)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Lock(connection, transaction);
        RequireAdmin(connection, transaction, actor);
        using var command = Command(connection, transaction,
            $"SELECT {Columns} FROM lss.Users ORDER BY CASE WHEN StatusCode='Pending' THEN 0 ELSE 1 END,Email,UserId");
        var users = new List<RegisteredUser>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) users.Add(Read(reader));
        transaction.Commit();
        return users;
    }

    private RegisteredUser RequireAdmin(SqlConnection connection, SqlTransaction transaction, ClaimsPrincipal actor)
    {
        var identity = Identity(actor) ?? throw new UnauthorizedAccessException();
        var user = Find(connection, transaction, $"{identity.Tenant:D}:{identity.Object:D}");
        return user?.Role == AppRole.Admin ? user : throw new UnauthorizedAccessException();
    }

    public void ChangeRole(ClaimsPrincipal actor, string key, AppRole role, long expectedVersion)
    {
        if (role is not (AppRole.Staff or AppRole.Revoked)) throw new ArgumentException("Choose Staff or Revoked. Use the separate administrator transfer action.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Lock(connection, transaction);
        var admin = RequireAdmin(connection, transaction, actor);
        var target = Find(connection, transaction, key) ?? throw new ArgumentException("User not found.");
        if (target.Role == AppRole.Admin) throw new ArgumentException("The active administrator cannot be revoked. Transfer administration first.");
        if (target.AuthorizationVersion != expectedVersion) throw new ArgumentException("Access changed since this page was loaded. Refresh and try again.");
        if (target.Role == role) { transaction.Commit(); return; }
        var status = role == AppRole.Staff ? "Approved" : "Revoked";
        var databaseRole = role == AppRole.Staff ? "Staff" : target.DatabaseRole;
        UpdateAccess(connection, transaction, admin, target, status, databaseRole,
            role == AppRole.Revoked ? "Revoked" : target.Status == "Revoked" ? "Restored" : "Approved",
            "Access updated by the LSS administrator.", Guid.NewGuid());
        transaction.Commit();
    }

    public void TransferAdmin(ClaimsPrincipal actor, string key, long expectedVersion)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Lock(connection, transaction);
        var admin = RequireAdmin(connection, transaction, actor);
        var successor = Find(connection, transaction, key) ?? throw new ArgumentException("User not found.");
        if (successor.Role != AppRole.Staff) throw new ArgumentException("The successor must be approved Staff.");
        if (successor.AuthorizationVersion != expectedVersion) throw new ArgumentException("Access changed. Refresh and try again.");
        var correlation = Guid.NewGuid();
        UpdateAccess(connection, transaction, admin, admin, "Approved", "Staff", "AdminTransferred", "Administration transferred to another approved user.", correlation);
        UpdateAccess(connection, transaction, admin, successor, "Approved", "Admin", "AdminTransferred", "Administration received from the previous administrator.", correlation);
        using var check = Command(connection, transaction, "SELECT COUNT(*) FROM lss.Users WHERE StatusCode='Approved' AND RoleCode='Admin'");
        if ((int)check.ExecuteScalar()! != 1) throw new InvalidOperationException("Administrator transfer failed.");
        transaction.Commit();
    }

    private static void UpdateAccess(SqlConnection connection, SqlTransaction transaction, RegisteredUser actor,
        RegisteredUser target, string status, string role, string action, string reason, Guid correlation)
    {
        using var command = Command(connection, transaction, """
            UPDATE lss.Users SET StatusCode=@status,RoleCode=@role,AuthorizationVersion=AuthorizationVersion+1,
                UpdatedAtUtc=SYSUTCDATETIME(),AccessChangedAtUtc=SYSUTCDATETIME(),AccessChangedByUserId=@actor
                WHERE UserId=@id AND RowVersion=@version;
            """, ("@status", status), ("@role", role), ("@actor", actor.Id), ("@id", target.Id), ("@version", target.RowVersion));
        if (command.ExecuteNonQuery() != 1) throw new ArgumentException("The user was changed concurrently. Refresh and try again.");
        Audit(connection, transaction, target.Id, actor.Id, actor.Key, action, target.Status,
            target.DatabaseRole, status, role, reason, correlation);
    }

    private static void Audit(SqlConnection connection, SqlTransaction transaction, long target, long? actor,
        string actorReference, string action, string? previousStatus, string? previousRole,
        string status, string role, string reason, Guid correlation)
    {
        using var command = Command(connection, transaction, """
            INSERT lss.AccessAudit (TargetUserId,ActorUserId,ActorKind,ActorReference,ActionCode,
                PreviousStatusCode,PreviousRoleCode,NewStatusCode,NewRoleCode,Reason,CorrelationId)
            VALUES (@target,@actor,@kind,@reference,@action,@previousStatus,@previousRole,@status,@role,@reason,@correlation);
            """, ("@target", target), ("@actor", actor), ("@kind", actor is null ? "System" : "User"),
            ("@reference", actorReference), ("@action", action), ("@previousStatus", previousStatus),
            ("@previousRole", previousRole), ("@status", status), ("@role", role), ("@reason", reason), ("@correlation", correlation));
        command.ExecuteNonQuery();
    }
}

