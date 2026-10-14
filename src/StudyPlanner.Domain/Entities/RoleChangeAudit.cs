using StudyPlanner.Domain.Common;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Domain.Entities;

public sealed class RoleChangeAudit : Entity
{
    public Guid UserId { get; private set; }
    public UserRole PreviousRole { get; private set; }
    public UserRole NewRole { get; private set; }
    public string Operator { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public DateTime ChangedAt { get; private set; }
    private RoleChangeAudit() { }
    public RoleChangeAudit(Guid userId, UserRole previousRole, UserRole newRole, string actor, string reason, DateTime changedAt)
    { UserId = userId; PreviousRole = previousRole; NewRole = newRole; Operator = actor; Reason = reason; ChangedAt = changedAt; }
}
