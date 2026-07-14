using MediatR;
using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Application.Queries.Categories;
public record GetAllCategoriesQuery(int Page = 1, int PageSize = 100) : IRequest<PaginatedResult<CategoryDto>>;
