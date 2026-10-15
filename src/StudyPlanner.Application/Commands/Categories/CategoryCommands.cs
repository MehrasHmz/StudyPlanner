using MediatR;

namespace StudyPlanner.Application.Commands.Categories;
public record CreateCategoryCommand(string Name, string? Description, bool IsShared = false) : IRequest<Guid>;
public record UpdateCategoryCommand(Guid Id, string Name, string? Description) : IRequest;
public record DeleteCategoryCommand(Guid Id) : IRequest;
