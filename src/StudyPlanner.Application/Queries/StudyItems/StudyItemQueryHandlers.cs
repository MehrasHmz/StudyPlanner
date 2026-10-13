using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Queries.StudyItems;
public class GetStudyItemByIdHandler : IRequestHandler<GetStudyItemByIdQuery, StudyItemDto?>
{
    private readonly IStudyItemRepository _repo;
    public GetStudyItemByIdHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task<StudyItemDto?> Handle(GetStudyItemByIdQuery r, CancellationToken ct)
    {
        var item = await _repo.GetWithDetailsAsync(r.Id, ct);
        return item == null || r.UserId == Guid.Empty || item.UserId != r.UserId || item.IsDeleted ? null : Map(item);
    }
    private static StudyItemDto Map(Domain.Entities.StudyItem i) =>
        new(i.Id, i.Title, i.Description, i.Type, i.Difficulty, i.EstimatedDurationMinutes, i.Priority,
            i.NextReviewDate, i.IsCompleted, i.CategoryId, i.Category?.IsDeleted == true ? "Archived category" : i.Category?.Name ?? "", i.UserId, i.CreatedAt);
}
public class GetStudyItemsHandler : IRequestHandler<GetStudyItemsQuery, PaginatedResult<StudyItemDto>>
{
    private readonly IStudyItemRepository _repo;
    public GetStudyItemsHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task<PaginatedResult<StudyItemDto>> Handle(GetStudyItemsQuery r, CancellationToken ct)
    {
        var page = Math.Clamp(r.Page, 1, 1_000_000); var size = Math.Clamp(r.PageSize, 1, 100);
        var (items, total) = await _repo.GetPagedAsync(r.UserId, page, size, r.Search, r.SortBy, r.Desc, ct);
        return new PaginatedResult<StudyItemDto>(items.Select(Map).ToList(), total, page, size);
    }
    private static StudyItemDto Map(Domain.Entities.StudyItem i) =>
        new(i.Id, i.Title, i.Description, i.Type, i.Difficulty, i.EstimatedDurationMinutes, i.Priority,
            i.NextReviewDate, i.IsCompleted, i.CategoryId, i.Category?.IsDeleted == true ? "Archived category" : i.Category?.Name ?? "", i.UserId, i.CreatedAt);
}
