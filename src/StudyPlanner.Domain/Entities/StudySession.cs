using StudyPlanner.Domain.Common;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Domain.Entities;
public class StudySession : AuditableEntity
{
    public Guid UserId { get; private set; }
    public DateTime PlannedDate { get; private set; }
    public int AvailableMinutes { get; private set; }
    public SessionStatus Status { get; private set; }
    public int TotalPlannedItems { get; private set; }
    public int TotalPlannedMinutes { get; private set; }
    public User User { get; private set; } = null!;
    private readonly List<StudySessionItem> _sessionItems = new();
    public IReadOnlyCollection<StudySessionItem> SessionItems => _sessionItems.AsReadOnly();

    private StudySession() : base() { }
    public StudySession(Guid userId, DateTime plannedDate, int availableMinutes) : base()
    {
        UserId = userId; PlannedDate = plannedDate; AvailableMinutes = availableMinutes;
        Status = SessionStatus.Planned;
    }
    public void AddItem(StudySessionItem item) { _sessionItems.Add(item); RecalculateTotals(); }
    public void ClearItems() { _sessionItems.Clear(); TotalPlannedItems = 0; TotalPlannedMinutes = 0; }
    public void StartSession() { if (Status != SessionStatus.Planned) throw new InvalidOperationException("Only planned sessions can start."); Status = SessionStatus.InProgress; }
    public void CompleteSession() { if (Status != SessionStatus.InProgress) throw new InvalidOperationException("Only in-progress sessions can complete."); Status = SessionStatus.Completed; }
    public void CancelSession() { if (Status == SessionStatus.Completed) throw new InvalidOperationException("Cannot cancel completed."); Status = SessionStatus.Cancelled; }
    public bool HasTimeFor(int minutes) => TotalPlannedMinutes + minutes <= AvailableMinutes;
    private void RecalculateTotals() { TotalPlannedItems = _sessionItems.Count; TotalPlannedMinutes = _sessionItems.Sum(i => i.PlannedMinutes); }
}
