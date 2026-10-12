using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Queries.StudyItems;
using StudyPlanner.Web.Models;

namespace StudyPlanner.Web.Controllers;

[Authorize]
public class FocusController : BaseController
{
    private readonly IMediator _mediator;
    public FocusController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Index(Guid? itemId)
    {
        var owner = GetUserId();
        string? title = null;
        if (itemId.HasValue)
        {
            var item = await _mediator.Send(new GetStudyItemByIdQuery(itemId.Value, owner), HttpContext.RequestAborted);
            if (item == null) return NotFound();
            title = item.Title;
        }
        var items = await _mediator.Send(new GetStudyItemsQuery(owner, 1, 100), HttpContext.RequestAborted);
        return View(new FocusViewModel(items.Items, title, itemId));
    }
}
