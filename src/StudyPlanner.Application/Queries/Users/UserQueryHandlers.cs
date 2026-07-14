using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Queries.Users;
public class GetDashboardHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private readonly IStudyItemRepository _items;
    private readonly IStudySessionRepository _sessions;
    private readonly IProgressRecordRepository _progress;
    public GetDashboardHandler(IStudyItemRepository items, IStudySessionRepository sessions, IProgressRecordRepository progress)
    { _items = items; _sessions = sessions; _progress = progress; }

    public async Task<DashboardDto> Handle(GetDashboardQuery r, CancellationToken ct)
    {
        var items = await _items.GetByUserIdAsync(r.UserId, ct);
        var sessions = await _sessions.GetByUserIdAsync(r.UserId, ct);
        var totalMinutes = await _progress.GetTotalMinutesAsync(r.UserId, ct);
        var now = DateTime.UtcNow;
        var dueItems = items.Where(i => !i.IsCompleted && i.IsDueForReview(now)).ToList();
        var recent = items.OrderByDescending(i => i.CreatedAt).Take(5).Select(i =>
            new StudyItemDto(i.Id, i.Title, i.Description, i.Type, i.Difficulty, i.EstimatedDurationMinutes, i.Priority,
                i.NextReviewDate, i.IsCompleted, i.CategoryId, i.Category?.Name ?? "", i.UserId, i.CreatedAt)).ToList();
        return new DashboardDto(items.Count, items.Count(i => i.IsCompleted), dueItems.Count, sessions.Count, totalMinutes, recent);
    }
}
