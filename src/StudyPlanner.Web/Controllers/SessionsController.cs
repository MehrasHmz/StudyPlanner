using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Web.Controllers;
[Authorize]
public class SessionsController : BaseController
{
    private readonly IMediator _mediator;
    public SessionsController(IMediator mediator) => _mediator = mediator;

    public IActionResult Index() => View();

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
}
