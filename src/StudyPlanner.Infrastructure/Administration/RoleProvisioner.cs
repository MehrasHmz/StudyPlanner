using Microsoft.EntityFrameworkCore;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.Infrastructure.Administration;

public record RoleTarget(Guid UserId, UserRole CurrentRole, UserRole RequestedRole, bool Active);
public sealed class RoleProvisioner
{
    private readonly AppDbContext _db;
    public RoleProvisioner(AppDbContext db) => _db = db;
    public async Task<RoleTarget> InspectAsync(Guid id, UserRole role, CancellationToken ct = default)
    {
        ValidateTarget(id, role);
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new InvalidOperationException("Target account not found.");
        ValidateAccount(user, role);
        return new(id, user.Role, role, user.IsActive);
    }
    public async Task<bool> ApplyAsync(Guid id, UserRole role, string actor, string reason, bool confirmed, CancellationToken ct = default)
    {
        ValidateTarget(id, role);
        if (!confirmed) throw new InvalidOperationException("Explicit operator confirmation is required.");
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200 || actor.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl))
            throw new ArgumentException("Supply a bounded operator identity and reason without control characters.");
        // Serialize operator changes to the same account; role and durable audit commit together.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var users = await _db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
            var user = users.SingleOrDefault() ?? throw new InvalidOperationException("Target account not found.");
            await _db.Entry(user).ReloadAsync(ct);
            ValidateAccount(user, role);
            if (user.Role == role) { await transaction.CommitAsync(ct); return false; }
            var previous = user.Role;
            user.SetRole(role);
            _db.Set<RoleChangeAudit>().Add(new(id, previous, role, actor.Trim(), reason.Trim(), DateTime.UtcNow));
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch { _db.ChangeTracker.Clear(); throw; }
    }
    private static void ValidateTarget(Guid id, UserRole role)
    {
        if (id == Guid.Empty || !Enum.IsDefined(role)) throw new ArgumentException("Use an exact existing user UUID and a known role.");
    }
    private static void ValidateAccount(User user, UserRole requested)
    {
        if (!Enum.IsDefined(user.Role)) throw new InvalidOperationException("Invalid stored role; investigate before provisioning.");
        if (requested == UserRole.Administrator && !user.IsActive) throw new InvalidOperationException("Cannot grant administrator to an inactive account.");
    }
}
