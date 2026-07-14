using StudyPlanner.Domain.Common;

namespace StudyPlanner.Domain.Entities;
public class ProgressRecord : AuditableEntity
{
    public Guid UserId { get; private set; }
    public Guid StudyItemId { get; private set; }
    public Guid? SessionId { get; private set; }
    public DateTime CompletedAt { get; private set; }
    public int MinutesSpent { get; private set; }
    public double? ResultScore { get; private set; }
    public string Notes { get; private set; } = string.Empty;
    public User User { get; private set; } = null!;
    public StudyItem StudyItem { get; private set; } = null!;

    private ProgressRecord() : base() { }
    public ProgressRecord(Guid userId, Guid studyItemId, int minutesSpent, Guid? sessionId = null, double? score = null, string notes = "") : base()
    {
        UserId = userId; StudyItemId = studyItemId; SessionId = sessionId;
        CompletedAt = DateTime.UtcNow; MinutesSpent = minutesSpent;
        ResultScore = score; Notes = notes?.Trim() ?? string.Empty;
    }
}
