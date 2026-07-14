using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Application.Queries.StudyItems;
public record GetStudyItemByIdQuery(Guid Id) : IRequest<StudyItemDto?>;
public record GetStudyItemsQuery(Guid UserId, int Page = 1, int PageSize = 10, string? Search = null,
    string? SortBy = null, bool Desc = false) : IRequest<PaginatedResult<StudyItemDto>>;
