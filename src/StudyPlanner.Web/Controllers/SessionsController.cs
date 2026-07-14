using MediatR;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Web.Controllers;
public class SessionsController : Controller
{
    private readonly IMediator _mediator;
    public SessionsController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index()
    {
        ViewBag.UserId = GetUserId();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Plan(int AvailableMinutes)
    {
        try
        {
            var result = await _mediator.Send(new PlanSessionCommand(GetUserId(), AvailableMinutes));
            return View("Plan", result);
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CompleteSession(Guid SessionId, List<ItemResultDto> Results)
    {
        try
        {
            // Filter out empty results (from form fields that weren't filled)
            var validResults = Results.Where(r => r.StudyItemId != Guid.Empty && r.MinutesSpent > 0).ToList();
            if (validResults.Any())
            {
                await _mediator.Send(new CompleteSessionCommand(SessionId, validResults));
            }
            TempData["Success"] = "Session completed! Items marked as reviewed.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    private static Guid GetUserId() => Guid.Parse("00000000-0000-0000-0000-000000000001");
}
