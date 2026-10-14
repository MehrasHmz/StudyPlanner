using Microsoft.EntityFrameworkCore;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.Infrastructure.Administration;

public interface IReleaseAccountCheck { Task VerifyAsync(CancellationToken ct = default); }
public sealed class DemoAccountRelease : IReleaseAccountCheck
{
    public static readonly Guid HistoricalId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly AppDbContext _db;
    public DemoAccountRelease(AppDbContext db) => _db = db;
    public async Task VerifyAsync(CancellationToken ct = default)
    {
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Id == HistoricalId && u.IsActive, ct))
            throw new InvalidOperationException("Active historical demo account blocks Production startup. Inspect the target and run the approved operator release procedure.");
    }
    public async Task<User?> InspectAsync(Guid id, CancellationToken ct = default)
    {
        if (id != HistoricalId) throw new ArgumentException("Only the exact historical seed UUID is supported.");
        return await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
    }
    public async Task<bool> DisableAsync(Guid id, string actor, string reason, bool confirmed, CancellationToken ct = default)
    {
        if (id != HistoricalId || !confirmed) throw new ArgumentException("Exact seed UUID and explicit confirmation required.");
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200 || actor.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl)) throw new ArgumentException("Invalid operator metadata.");
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var rows = await _db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
            var user = rows.SingleOrDefault() ?? throw new InvalidOperationException("Historical account is absent. No action required; verify target environment.");
            await _db.Entry(user).ReloadAsync(ct);
            if (!Enum.IsDefined(user.Role)) throw new InvalidOperationException("Invalid stored role.");
            if (!user.IsActive && user.Role == UserRole.Regular) { await transaction.CommitAsync(ct); return false; }
            if (user.Role != UserRole.Regular)
            {
                _db.Set<RoleChangeAudit>().Add(new(id, user.Role, UserRole.Regular, actor.Trim(), reason.Trim(), DateTime.UtcNow));
                user.SetRole(UserRole.Regular);
            }
            user.Deactivate();
            await _db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
        }
        catch { _db.ChangeTracker.Clear(); throw; }
    }
}
