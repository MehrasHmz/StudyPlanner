using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.ReadModels;

namespace StudyPlanner.Application.Queries.Sessions;

public record GetSessionHistoryQuery(Guid UserId, int Page = 1, int PageSize = 10) : IRequest<PaginatedResult<SessionHistoryEntry>>;
public record GetSessionDetailsQuery(Guid SessionId, Guid UserId) : IRequest<SessionDetailsDto?>;
public record SessionDetailsDto(Guid UserId, StudySessionDto Session, List<ProgressRecordDto> Activities)
{
    public int? RecordedMinutes => Activities.Count == 0 ? null : Activities.Sum(p => p.MinutesSpent);
    public int CompletedItems => Activities.Select(p => p.StudyItemId).Distinct().Count();
}
