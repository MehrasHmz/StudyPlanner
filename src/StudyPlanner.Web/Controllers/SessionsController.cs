using FluentValidation;
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
        if (!ModelState.IsValid) return BadRequest("Invalid session results.");
        try
        {
            await _mediator.Send(new CompleteSessionCommand(SessionId, GetUserId(), Results), HttpContext.RequestAborted);
            TempData["Success"] = "Session completed! Items marked as reviewed.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException)
        {
            return BadRequest("Invalid session results.");
        }
        catch (SessionCompletionConflictException)
        {
            return Conflict("The session changed while being completed. Reload the session.");
        }
        catch (InvalidSessionCompletionException)
        {
            return BadRequest("Session cannot be completed with these results.");
        }
        return RedirectToAction(nameof(Index));
    }
}
