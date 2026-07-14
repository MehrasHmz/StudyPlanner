using MediatR;
using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Application.Commands.Sessions;
public record PlanSessionCommand(Guid UserId, int AvailableMinutes) : IRequest<StudySessionDto>;
public record CompleteSessionCommand(Guid SessionId, List<ItemResultDto> Results) : IRequest;
public record ItemResultDto(Guid StudyItemId, int MinutesSpent, double? Score, string Notes);
