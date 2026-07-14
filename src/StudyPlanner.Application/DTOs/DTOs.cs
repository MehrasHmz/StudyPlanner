using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Application.DTOs;
public record UserDto(Guid Id, string FullName, string Email, DateTime CreatedAt);
public record AuthDto(string AccessToken, UserDto User);
public record CategoryDto(Guid Id, string Name, string Description);
public record StudyItemDto(Guid Id, string Title, string Description, StudyItemType Type, DifficultyLevel Difficulty,
    int EstimatedDurationMinutes, int Priority, DateTime? NextReviewDate, bool IsCompleted, Guid CategoryId,
    string CategoryName, Guid UserId, DateTime CreatedAt);
public record StudySessionDto(Guid Id, DateTime PlannedDate, int AvailableMinutes, SessionStatus Status,
    int TotalPlannedItems, int TotalPlannedMinutes, DateTime CreatedAt, List<StudySessionItemDto> Items);
public record StudySessionItemDto(Guid Id, Guid StudyItemId, string StudyItemTitle, int PlannedMinutes, int Order);
public record ProgressRecordDto(Guid Id, Guid StudyItemId, string Title, DateTime CompletedAt, int MinutesSpent, double? Score, string Notes);
public record DashboardDto(int TotalItems, int CompletedItems, int DueForReview, int TotalSessions, int TotalMinutesStudied, List<StudyItemDto> RecentItems);
public record PaginatedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
