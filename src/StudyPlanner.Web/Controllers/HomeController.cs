using MediatR;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Users;

namespace StudyPlanner.Web.Controllers;
public class HomeController : Controller
{
    private readonly IMediator _mediator;
    public HomeController(IMediator mediator) => _mediator = mediator;
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var dashboard = await _mediator.Send(new GetDashboardQuery(userId));
        return View(dashboard);
    }
    private static Guid GetUserId() => Guid.Parse("00000000-0000-0000-0000-000000000001");
}
