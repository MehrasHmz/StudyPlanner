using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Queries.Users;
public class GetDashboardHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private readonly IStudyItemRepository _items;
    private readonly IStudySessionRepository _sessions;
    private readonly IProgressRecordRepository _progress;
    private readonly TimeProvider _clock;
    public GetDashboardHandler(IStudyItemRepository items, IStudySessionRepository sessions, IProgressRecordRepository progress, TimeProvider? clock = null)
    { _items = items; _sessions = sessions; _progress = progress; _clock = clock ?? TimeProvider.System; }

    public async Task<DashboardDto> Handle(GetDashboardQuery r, CancellationToken ct)
    {
        var items = (await _items.GetByUserIdAsync(r.UserId, ct)).Where(i => i.UserId == r.UserId && !i.IsDeleted).ToList();
        var sessions = await _sessions.GetByUserIdAsync(r.UserId, ct);
        var totalMinutes = await _progress.GetTotalMinutesAsync(r.UserId, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var dueItems = items.Where(i => i.IsDueForReview(now)).ToList();
        var recent = items.OrderByDescending(i => i.CreatedAt).Take(5).Select(i =>
            new StudyItemDto(i.Id, i.Title, i.Description, i.Type, i.Difficulty, i.EstimatedDurationMinutes, i.Priority,
                i.NextReviewDate, i.IsCompleted, i.CategoryId, i.Category?.Name ?? "", i.UserId, i.CreatedAt)).ToList();
        return new DashboardDto(items.Count, items.Count(i => i.IsCompleted), dueItems.Count, sessions.Count, totalMinutes, recent) { AsOfUtc = now };
    }
}
