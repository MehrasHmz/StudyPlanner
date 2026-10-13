using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Domain.ReadModels;

namespace StudyPlanner.Application.Queries.Sessions;

public class GetSessionHistoryHandler : IRequestHandler<GetSessionHistoryQuery, PaginatedResult<SessionHistoryEntry>>
{
    private readonly IStudySessionRepository _sessions;
    public GetSessionHistoryHandler(IStudySessionRepository sessions) => _sessions = sessions;
    public async Task<PaginatedResult<SessionHistoryEntry>> Handle(GetSessionHistoryQuery request, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty) throw new UnauthorizedAccessException();
        var page = Math.Clamp(request.Page, 1, 1_000_000);
        var size = Math.Clamp(request.PageSize, 1, 50);
        var (items, total) = await _sessions.GetHistoryPageAsync(request.UserId, page, size, ct);
        return new(items.Where(i => i.UserId == request.UserId).ToList(), total, page, size);
    }
}

public class GetSessionDetailsHandler : IRequestHandler<GetSessionDetailsQuery, SessionDetailsDto?>
{
    private readonly IStudySessionRepository _sessions;
    private readonly IProgressRecordRepository _progress;
    public GetSessionDetailsHandler(IStudySessionRepository sessions, IProgressRecordRepository progress)
    { _sessions = sessions; _progress = progress; }
    public async Task<SessionDetailsDto?> Handle(GetSessionDetailsQuery request, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty) return null;
        var session = await _sessions.GetOwnedWithItemsAsync(request.SessionId, request.UserId, ct);
        if (session == null || session.UserId != request.UserId) return null;
        var items = session.SessionItems.OrderBy(i => i.Order).Select(i => new StudySessionItemDto(i.Id, i.StudyItemId,
            i.StudyItem?.UserId == request.UserId ? i.StudyItem.Title : "Unavailable study item", i.PlannedMinutes, i.Order)).ToList();
        var records = await _progress.GetBySessionAsync(request.UserId, session.Id, ct);
        var activities = records.Where(p => p.UserId == request.UserId && p.SessionId == session.Id)
            .Select(p => new ProgressRecordDto(p.Id, p.StudyItemId,
                p.StudyItem?.UserId == request.UserId ? p.StudyItem.Title : items.FirstOrDefault(i => i.StudyItemId == p.StudyItemId)?.StudyItemTitle ?? "Unavailable study item",
                p.CompletedAt, p.MinutesSpent, p.ResultScore, p.Notes)).ToList();
        return new(request.UserId, new(session.Id, session.PlannedDate, session.AvailableMinutes, session.Status,
            session.TotalPlannedItems, session.TotalPlannedMinutes, session.CreatedAt, items), activities);
    }
}
