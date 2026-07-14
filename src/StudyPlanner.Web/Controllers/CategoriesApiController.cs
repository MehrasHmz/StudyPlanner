using MediatR;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Queries.Categories;

namespace StudyPlanner.Web.Controllers;
[ApiController]
[Route("api/categories")]
public class CategoriesApiController : ControllerBase
{
    private readonly IMediator _mediator;
    public CategoriesApiController(IMediator mediator) => _mediator = mediator;

    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        var result = await _mediator.Send(new GetAllCategoriesQuery(1, 100));
        return Ok(result.Items);
    }
}
