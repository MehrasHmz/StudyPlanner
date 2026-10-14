using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.UnitTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresTestDatabase.ConnectionVariable)))
            Skip = "Set STUDYPLANNER_TEST_POSTGRES to an explicitly approved disposable PostgreSQL server.";
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresTestDatabase.ConnectionVariable)))
            Skip = "Set STUDYPLANNER_TEST_POSTGRES to an explicitly approved disposable PostgreSQL server.";
    }
}

public sealed class PostgresTestDatabase : IAsyncLifetime
{
    public const string ConnectionVariable = "STUDYPLANNER_TEST_POSTGRES";
    private readonly string? _adminConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
    private readonly string _name = "studyplanner_phase3_" + Guid.NewGuid().ToString("N");
    private string? _testConnection;
    private bool _created;
    public bool InitialMigrationOnly { get; init; }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminConnection)) return;
        var builder = new NpgsqlConnectionStringBuilder(_adminConnection)
        {
            Pooling = false, Timeout = 5, CommandTimeout = 15, IncludeErrorDetail = false
        };
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", admin))
            await command.ExecuteNonQueryAsync();
        _created = true;
        builder.Database = _name;
        _testConnection = builder.ConnectionString;
        try
        {
            await using var context = CreateContext();
            if (InitialMigrationOnly)
                await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260712092810_InitialCreate");
            else await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext(params IInterceptor[] interceptors) => CreateContext(true, interceptors);

    public AppDbContext CreateContext(bool singleStatementBatches, params IInterceptor[] interceptors)
    {
        if (_testConnection == null) throw new InvalidOperationException("PostgreSQL test environment is not configured.");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_testConnection, provider =>
            {
                provider.CommandTimeout(15);
                if (singleStatementBatches) provider.MaxBatchSize(1);
            })
            .AddInterceptors(interceptors).Options;
        return new AppDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (!_created) return;
        var builder = new NpgsqlConnectionStringBuilder(_adminConnection!)
        {
            Pooling = false, Timeout = 5, CommandTimeout = 15, IncludeErrorDetail = false
        };
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        // Only the unpredictable database created by this fixture is ever dropped.
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_name}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
