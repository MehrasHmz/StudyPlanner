using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Domain.ReadModels;

public record SessionHistoryEntry(Guid Id, Guid UserId, DateTime PlannedDate, SessionStatus Status,
    int AvailableMinutes, int PlannedMinutes, int PlannedItems, int? RecordedMinutes, int CompletedItems);
