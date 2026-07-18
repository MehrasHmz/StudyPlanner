using MediatR;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Users;

namespace StudyPlanner.Web.Controllers;
public class HomeController : BaseController
{
    private readonly IMediator _mediator;
    public HomeController(IMediator mediator) => _mediator = mediator;
    public async Task<IActionResult> Index()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return View("Landing");

        var dashboard = await _mediator.Send(new GetDashboardQuery(GetUserId()));
        return View(dashboard);
    }
}
