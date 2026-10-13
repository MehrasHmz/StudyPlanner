using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Web.Models;
using StudyPlanner.Application.Queries.Sessions;
using StudyPlanner.Domain.Enums;
using System.Text.RegularExpressions;

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
            return View("Plan", SessionCompletionViewModel.Create(result));
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
        if (!ModelState.IsValid) return await CompletionErrors(SessionId, Results);
        try
        {
            await _mediator.Send(new CompleteSessionCommand(SessionId, GetUserId(), Results), HttpContext.RequestAborted);
            TempData["Success"] = "Session completed! Items marked as reviewed.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            return await CompletionErrors(SessionId, Results, ex);
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

    [HttpGet]
    public async Task<IActionResult> History(int page = 1) => View(await _mediator.Send(new GetSessionHistoryQuery(GetUserId(), page), HttpContext.RequestAborted));

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var details = await _mediator.Send(new GetSessionDetailsQuery(id, GetUserId()), HttpContext.RequestAborted);
        return details == null || details.UserId != GetUserId() ? NotFound() : View(details);
    }

    [HttpGet]
    public async Task<IActionResult> Record(Guid id)
    {
        var details = await _mediator.Send(new GetSessionDetailsQuery(id, GetUserId()), HttpContext.RequestAborted);
        if (details == null || details.UserId != GetUserId()) return NotFound();
        if (details.Session.Status != SessionStatus.Planned) return RedirectToAction(nameof(Details), new { id });
        return View("Plan", SessionCompletionViewModel.Create(details.Session));
    }

    private async Task<IActionResult> CompletionErrors(Guid id, List<ItemResultDto>? results, ValidationException? validation = null)
    {
        var details = await _mediator.Send(new GetSessionDetailsQuery(id, GetUserId()), HttpContext.RequestAborted);
        if (details == null || details.UserId != GetUserId()) return BadRequest("Invalid session results.");
        if (details.Session.Status != SessionStatus.Planned) return BadRequest("Session cannot be completed with these results.");
        var model = SessionCompletionViewModel.Create(details.Session, results);
        if (validation != null)
        {
            foreach (var error in validation.Errors)
            {
                var match = Regex.Match(error.PropertyName, @"^Results\[(\d+)\]\.(StudyItemId|MinutesSpent|Score|Notes)$");
                var key = "";
                if (match.Success && int.TryParse(match.Groups[1].Value, out var index) && results != null && index < results.Count)
                {
                    var row = model.Results.FindIndex(r => r.StudyItemId == results[index].StudyItemId);
                    if (row >= 0) key = $"Results[{row}].{match.Groups[2].Value}";
                }
                ModelState.AddModelError(key, error.ErrorMessage);
            }
        }
        if (results == null || results.Count == 0) ModelState.AddModelError("", "Select at least one item and enter its actual study time.");
        Response.StatusCode = 400;
        return View("Plan", model);
    }
}
