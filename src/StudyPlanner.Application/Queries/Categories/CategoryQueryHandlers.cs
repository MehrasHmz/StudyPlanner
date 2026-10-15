using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Application.Authorization;

namespace StudyPlanner.Application.Queries.Categories;
public class GetAllCategoriesHandler : IRequestHandler<GetAllCategoriesQuery, PaginatedResult<CategoryDto>>
{
    private readonly ICategoryRepository _repo;
    private readonly ICurrentUser _caller;
    private readonly ICategoryPermission _permission;
    public GetAllCategoriesHandler(ICategoryRepository repo, ICurrentUser caller, ICategoryPermission permission) { _repo = repo; _caller = caller; _permission = permission; }
    public async Task<PaginatedResult<CategoryDto>> Handle(GetAllCategoriesQuery r, CancellationToken ct)
    {
        var page = Math.Clamp(r.Page, 1, 1_000_000); var size = Math.Clamp(r.PageSize, 1, 100);
        var cats = await _repo.GetPagedAsync(page, size, ct, _caller.UserId);
        var total = await _repo.GetCountAsync(ct, _caller.UserId);
        var sharedManager = await _permission.CanManageAsync(ct);
        return new PaginatedResult<CategoryDto>(cats.Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.UserId,
            c.UserId.HasValue ? c.UserId == _caller.UserId : sharedManager)).ToList(), total, page, size);
    }
}
public class GetCategoryByIdHandler : IRequestHandler<GetCategoryByIdQuery, CategoryDto?>
{
    private readonly ICategoryRepository _repo;
    private readonly ICurrentUser _caller;
    private readonly ICategoryPermission _permission;
    public GetCategoryByIdHandler(ICategoryRepository repo, ICurrentUser caller, ICategoryPermission permission) { _repo = repo; _caller = caller; _permission = permission; }
    public async Task<CategoryDto?> Handle(GetCategoryByIdQuery request, CancellationToken ct)
    {
        var category = await _repo.GetByIdAsync(request.Id, ct);
        if (category == null || category.IsDeleted || (category.UserId.HasValue && category.UserId != _caller.UserId)) return null;
        return new CategoryDto(category.Id, category.Name, category.Description, category.UserId,
            category.UserId.HasValue ? category.UserId == _caller.UserId : await _permission.CanManageAsync(ct));
    }
}
