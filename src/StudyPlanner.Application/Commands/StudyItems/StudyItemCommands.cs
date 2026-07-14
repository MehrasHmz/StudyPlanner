using MediatR;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Application.Commands.StudyItems;
public record CreateStudyItemCommand(string Title, string Description, StudyItemType Type, DifficultyLevel Difficulty,
    int EstimatedDurationMinutes, int Priority, Guid CategoryId, Guid UserId, DateTime? NextReviewDate) : IRequest<Guid>;
public record UpdateStudyItemCommand(Guid Id, string Title, string Description, StudyItemType Type, DifficultyLevel Difficulty,
    int EstimatedDurationMinutes, int Priority, Guid CategoryId, DateTime? NextReviewDate) : IRequest;
public record DeleteStudyItemCommand(Guid Id) : IRequest;
public record ToggleCompleteCommand(Guid Id) : IRequest;
