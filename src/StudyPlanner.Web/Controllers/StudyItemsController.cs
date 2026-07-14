using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Categories;
using StudyPlanner.Application.Queries.StudyItems;

namespace StudyPlanner.Web.Controllers;
public class StudyItemsController : Controller
{
    private readonly IMediator _mediator;
    public StudyItemsController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index(int page = 1, string? search = null, string? sortBy = null, bool desc = false)
    {
        var result = await _mediator.Send(new GetStudyItemsQuery(GetUserId(), page, 10, search, sortBy, desc));
        return View(result);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Categories = await LoadCategories();
        ViewBag.Errors = new List<string>();
        return View();
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var item = await _mediator.Send(new GetStudyItemByIdQuery(id));
        if (item == null) return NotFound();
        ViewBag.Categories = await LoadCategories();
        ViewBag.Errors = new List<string>();
        return View(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateStudyItemCommand cmd)
    {
        try
        {
            await _mediator.Send(cmd with { UserId = GetUserId() });
            TempData["Success"] = "Study item created!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Categories = await LoadCategories();
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            return View(cmd);
        }
        catch (Exception ex)
        {
            ViewBag.Categories = await LoadCategories();
            ViewBag.Errors = new List<string> { ex.Message };
            return View(cmd);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UpdateStudyItemCommand cmd)
    {
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Study item updated!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Categories = await LoadCategories();
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            var item = await _mediator.Send(new GetStudyItemByIdQuery(cmd.Id));
            return View(item ?? new StudyItemDto(cmd.Id, cmd.Title, cmd.Description, cmd.Type, cmd.Difficulty,
                cmd.EstimatedDurationMinutes, cmd.Priority, cmd.NextReviewDate, false, cmd.CategoryId, "", GetUserId(), DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            ViewBag.Categories = await LoadCategories();
            ViewBag.Errors = new List<string> { ex.Message };
            var item = await _mediator.Send(new GetStudyItemByIdQuery(cmd.Id));
            return View(item);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteStudyItemCommand(id));
        TempData["Success"] = "Study item deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleComplete(Guid id)
    {
        await _mediator.Send(new ToggleCompleteCommand(id));
        return RedirectToAction(nameof(Index));
    }

    private static Guid GetUserId() => Guid.Parse("00000000-0000-0000-0000-000000000001");

    private async Task<List<CategoryDto>> LoadCategories()
    {
        var result = await _mediator.Send(new GetAllCategoriesQuery(1, 100));
        return result.Items.ToList();
    }
}
