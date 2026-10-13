using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Web.Models;

namespace StudyPlanner.Web.Controllers;
[Authorize]
public class SessionsController : BaseController
{
    private readonly IMediator _mediator;
    public SessionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public IActionResult Index() => View(new PlanSessionViewModel { AvailableMinutes = 45 });

    [HttpGet]
    public IActionResult Plan() => View("Index", new PlanSessionViewModel { AvailableMinutes = 45 });

    [HttpPost]
    public async Task<IActionResult> Plan(PlanSessionViewModel model)
    {
        if (!ModelState.IsValid) return View("Index", model);
        try
        {
            var result = await _mediator.Send(new PlanSessionCommand(GetUserId(), model.AvailableMinutes), HttpContext.RequestAborted);
            return View("Plan", result);
        }
        catch (ValidationException ex)
        {
            foreach (var error in ex.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return View("Index", model);
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
