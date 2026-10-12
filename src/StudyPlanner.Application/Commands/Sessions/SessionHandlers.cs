using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Commands.Sessions;
public class PlanSessionHandler : IRequestHandler<PlanSessionCommand, StudySessionDto>
{
    private readonly IStudySessionRepository _sessionRepo;
    private readonly IStudyItemRepository _itemRepo;
    private readonly IStudySessionPlanner _planner;
    public PlanSessionHandler(IStudySessionRepository sessionRepo, IStudyItemRepository itemRepo, IStudySessionPlanner planner)
    { _sessionRepo = sessionRepo; _itemRepo = itemRepo; _planner = planner; }

    public async Task<StudySessionDto> Handle(PlanSessionCommand r, CancellationToken ct)
    {
        var session = new StudySession(r.UserId, DateTime.UtcNow, r.AvailableMinutes);
        var items = await _itemRepo.GetByUserIdAsync(r.UserId, ct);
        var selected = _planner.SelectItems(items, r.AvailableMinutes, DateTime.UtcNow);
        int order = 1;
        foreach (var item in selected)
        {
            session.AddItem(new StudySessionItem(session.Id, item.Id, item.EstimatedDurationMinutes, order++));
        }
        await _sessionRepo.AddAsync(session, ct);
        await _sessionRepo.SaveChangesAsync(ct);
        return MapSession(session, selected);
    }
    private static StudySessionDto MapSession(StudySession s, IReadOnlyList<StudyItem> items) =>
        new(s.Id, s.PlannedDate, s.AvailableMinutes, s.Status, s.TotalPlannedItems, s.TotalPlannedMinutes, s.CreatedAt,
            s.SessionItems.Select(si => new StudySessionItemDto(si.Id, si.StudyItemId,
                items.FirstOrDefault(i => i.Id == si.StudyItemId)?.Title ?? "", si.PlannedMinutes, si.Order)).ToList());
}
public class CompleteSessionHandler : IRequestHandler<CompleteSessionCommand>
{
    private readonly IStudySessionRepository _sessionRepo;
    private readonly IStudyItemRepository _itemRepo;
    private readonly IProgressRecordRepository _progressRepo;
    public CompleteSessionHandler(IStudySessionRepository sessionRepo, IStudyItemRepository itemRepo, IProgressRecordRepository progressRepo)
    { _sessionRepo = sessionRepo; _itemRepo = itemRepo; _progressRepo = progressRepo; }

    public async Task Handle(CompleteSessionCommand r, CancellationToken ct)
    {
        var session = await _sessionRepo.GetWithItemsAsync(r.SessionId, ct);
        if (session == null || r.UserId == Guid.Empty || session.UserId != r.UserId)
            throw new KeyNotFoundException("Session not found.");
        if (session.Status != SessionStatus.Planned)
            throw new InvalidSessionCompletionException("Only planned sessions can be completed.");
        if (r.Results == null || r.Results.Count == 0 ||
            r.Results.Any(result => result == null || result.StudyItemId == Guid.Empty || result.MinutesSpent <= 0) ||
            r.Results.Select(result => result.StudyItemId).Distinct().Count() != r.Results.Count)
            throw new InvalidSessionCompletionException("Invalid session results.");

        // Validate the entire request before changing any tracked entity or adding progress.
        var sessionItemIds = session.SessionItems.Select(item => item.StudyItemId).ToHashSet();
        var items = new List<(ItemResultDto Result, StudyItem Item)>();
        foreach (var result in r.Results)
        {
            if (!sessionItemIds.Contains(result.StudyItemId))
                throw new InvalidSessionCompletionException("Invalid session results.");
            var item = await _itemRepo.GetByIdAsync(result.StudyItemId, ct);
            if (item == null || item.UserId != r.UserId || item.IsDeleted)
                throw new InvalidSessionCompletionException("Invalid session results.");
            items.Add((result, item));
        }

        session.StartSession();
        foreach (var (result, item) in items)
        {
            await _progressRepo.AddAsync(new ProgressRecord(session.UserId, result.StudyItemId, result.MinutesSpent, session.Id, result.Score, result.Notes ?? string.Empty), ct);
            item.MarkCompleted();
            item.SetNextReviewDate(DateTime.UtcNow.AddDays(7));
            _itemRepo.Update(item);
        }
        session.CompleteSession();
        _sessionRepo.Update(session);
        await _sessionRepo.SaveChangesAsync(ct);
    }
}
