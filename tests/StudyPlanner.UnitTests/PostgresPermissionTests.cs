using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Administration;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.UnitTests;

public class PostgresPermissionTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresPermissionTests(PostgresTestDatabase database) => _database = database;
    [PostgresFact]
    public async Task DocumentedGrants_AllowRegistrationAndOperatorActionsButDenyRuntimeRoleAuditAndSchemaWrites()
    {
        var runtime = "sp_runtime_" + Guid.NewGuid().ToString("N"); var op = "sp_operator_" + Guid.NewGuid().ToString("N");
        var identifiers = new NpgsqlCommandBuilder(); var runtimeSql = identifiers.QuoteIdentifier(runtime); var operatorSql = identifiers.QuoteIdentifier(op);
        await using var context = _database.CreateContext(); await context.Database.OpenConnectionAsync();
        await Sql(context, $"CREATE ROLE {runtimeSql} NOLOGIN NOINHERIT; CREATE ROLE {operatorSql} NOLOGIN NOINHERIT;");
        try
        {
            var database = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).Database;
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deployment/least-privilege.sql"));
            var sql = string.Join('\n', File.ReadAllLines(path).Where(line => !line.StartsWith('\\')))
                .Replace(":\"runtime_role\"", runtimeSql).Replace(":\"operator_role\"", operatorSql).Replace(":'expected_database'", "@database");
            await Sql(context, sql, database);
            await Sql(context, $"SET ROLE {runtimeSql}");
            var user = new User("Restricted runtime", Guid.NewGuid() + "@example.test", "synthetic-hash"); context.Users.Add(user); await context.SaveChangesAsync();
            user.Role.Should().Be(UserRole.Regular);
            (await Record.ExceptionAsync(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"Role\" = 1 WHERE \"Id\" = {user.Id}"))).Should().BeOfType<PostgresException>();
            (await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync("SELECT count(*) FROM \"RoleChangeAudits\""))).Should().BeOfType<PostgresException>();
            (await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync("CREATE TABLE public.forbidden_release_probe (id integer)"))).Should().BeOfType<PostgresException>();
            await Sql(context, $"RESET ROLE; SET ROLE {operatorSql}"); context.ChangeTracker.Clear();
            (await new RoleProvisioner(context).ApplyAsync(user.Id, UserRole.Administrator, "permission-test", "approved synthetic grant", true)).Should().BeTrue();
            (await context.Set<RoleChangeAudit>().CountAsync(a => a.UserId == user.Id)).Should().Be(1);
            await new RoleProvisioner(context).ApplyAsync(user.Id, UserRole.Regular, "permission-test", "synthetic revoke", true);
            await new DemoAccountRelease(context).DisableAsync(DemoAccountRelease.HistoricalId, "permission-test", "synthetic release", true);
            await new DemoAccountRelease(context).VerifyAsync();
        }
        finally
        {
            // Only these unpredictable NOLOGIN fixture roles are cleaned up; neither can own schema objects.
            await Sql(context, $"RESET ROLE; DROP OWNED BY {runtimeSql}, {operatorSql}; DROP ROLE {runtimeSql}; DROP ROLE {operatorSql};");
        }
    }
    private static async Task Sql(AppDbContext context, string sql, string? database = null)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand(); command.CommandText = sql;
        if (database != null) { var parameter = command.CreateParameter(); parameter.ParameterName = "database"; parameter.Value = database; command.Parameters.Add(parameter); }
        await command.ExecuteNonQueryAsync();
    }
}
