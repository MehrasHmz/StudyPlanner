using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Queries.Categories;
public class GetAllCategoriesHandler : IRequestHandler<GetAllCategoriesQuery, PaginatedResult<CategoryDto>>
{
    private readonly ICategoryRepository _repo;
    public GetAllCategoriesHandler(ICategoryRepository repo) => _repo = repo;
    public async Task<PaginatedResult<CategoryDto>> Handle(GetAllCategoriesQuery r, CancellationToken ct)
    {
        var cats = await _repo.GetPagedAsync(r.Page, r.PageSize, ct);
        var total = await _repo.GetCountAsync(ct);
        return new PaginatedResult<CategoryDto>(cats.Select(c => new CategoryDto(c.Id, c.Name, c.Description)).ToList(), total, r.Page, r.PageSize);
    }
}
public class GetCategoryByIdHandler : IRequestHandler<GetCategoryByIdQuery, CategoryDto?>
{
    private readonly ICategoryRepository _repo;
    public GetCategoryByIdHandler(ICategoryRepository repo) => _repo = repo;
    public async Task<CategoryDto?> Handle(GetCategoryByIdQuery request, CancellationToken ct)
    {
        var category = await _repo.GetByIdAsync(request.Id, ct);
        return category == null || category.IsDeleted ? null : new CategoryDto(category.Id, category.Name, category.Description);
    }
}
