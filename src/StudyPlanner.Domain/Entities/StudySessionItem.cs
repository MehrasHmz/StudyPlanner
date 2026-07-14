using StudyPlanner.Domain.Common;

namespace StudyPlanner.Domain.Entities;
public class StudySessionItem : Entity
{
    public Guid StudySessionId { get; private set; }
    public Guid StudyItemId { get; private set; }
    public int PlannedMinutes { get; private set; }
    public int Order { get; private set; }
    public StudySession StudySession { get; private set; } = null!;
    public StudyItem StudyItem { get; private set; } = null!;

    private StudySessionItem() : base() { }
    public StudySessionItem(Guid sessionId, Guid itemId, int minutes, int order) : base()
    {
        StudySessionId = sessionId; StudyItemId = itemId; PlannedMinutes = minutes; Order = order;
    }
}
