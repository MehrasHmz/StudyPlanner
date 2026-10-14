using Microsoft.EntityFrameworkCore;

namespace StudyPlanner.Infrastructure.Data;

public interface IDatabaseSchemaCheck { Task VerifyAsync(CancellationToken ct = default); }
public sealed class DatabaseSchemaCheck : IDatabaseSchemaCheck
{
    private readonly AppDbContext _db;
    public DatabaseSchemaCheck(AppDbContext db) => _db = db;
    public async Task VerifyAsync(CancellationToken ct = default)
    {
        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet();
        var expected = _db.Database.GetMigrations().ToHashSet();
        if (!applied.SetEquals(expected))
            throw new InvalidOperationException("Database schema is incompatible. Run the approved migration job before starting this application.");
    }
}
