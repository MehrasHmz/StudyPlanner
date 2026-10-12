using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Categories;
using StudyPlanner.Application.Queries.StudyItems;

namespace StudyPlanner.Web.Controllers;
[Authorize]
public class StudyItemsController : BaseController
{
    private readonly IMediator _mediator;
    public StudyItemsController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index(int page = 1, string? search = null, string? sortBy = null, bool desc = false)
    {
        var result = await _mediator.Send(new GetStudyItemsQuery(GetUserId(), page, 10, search, sortBy, desc));
        return View(result);
    }

    public async Task<IActionResult> Create(Guid? categoryId = null)
    {
        ViewBag.Categories = await LoadCategories(categoryId);
        ViewBag.Errors = new List<string>();
        var categories = (List<CategoryDto>)ViewBag.Categories;
        var selected = categoryId.HasValue && categories.Any(category => category.Id == categoryId.Value) ? categoryId.Value : Guid.Empty;
        return View(new CreateStudyItemCommand("", null, StudyPlanner.Domain.Enums.StudyItemType.Other,
            StudyPlanner.Domain.Enums.DifficultyLevel.Beginner, 30, 3, selected, GetUserId(), null));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var item = await _mediator.Send(new GetStudyItemByIdQuery(id, GetUserId()));
        if (item == null || item.UserId != GetUserId()) return NotFound();
        ViewBag.Categories = await LoadCategories(item.CategoryId);
        ViewBag.Errors = new List<string>();
        return View(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateStudyItemCommand cmd)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await LoadCategories(cmd.CategoryId);
            return View(cmd);
        }
        try
        {
            await _mediator.Send(cmd with { UserId = GetUserId() });
            TempData["Success"] = "Study item created!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Categories = await LoadCategories(cmd.CategoryId);
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            foreach (var error in ex.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return View(cmd);
        }
        catch (Exception ex)
        {
            ViewBag.Categories = await LoadCategories(cmd.CategoryId);
            ViewBag.Errors = new List<string> { ex.Message };
            return View(cmd);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UpdateStudyItemCommand cmd)
    {
        cmd = cmd with { UserId = GetUserId() };
        var existing = await _mediator.Send(new GetStudyItemByIdQuery(cmd.Id, cmd.UserId));
        if (existing == null) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await LoadCategories(cmd.CategoryId);
            return View(existing);
        }
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Study item updated!";
            return RedirectToAction(nameof(Index));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            ViewBag.Categories = await LoadCategories(cmd.CategoryId);
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            foreach (var error in ex.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            var item = await _mediator.Send(new GetStudyItemByIdQuery(cmd.Id, cmd.UserId));
            if (item == null) return NotFound();
            return View(item);
        }
        catch (Exception ex)
        {
            ViewBag.Categories = await LoadCategories(cmd.CategoryId);
            ViewBag.Errors = new List<string> { ex.Message };
            var item = await _mediator.Send(new GetStudyItemByIdQuery(cmd.Id, cmd.UserId));
            if (item == null) return NotFound();
            return View(item);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _mediator.Send(new DeleteStudyItemCommand(id, GetUserId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        TempData["Success"] = "Study item deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleComplete(Guid id)
    {
        try
        {
            await _mediator.Send(new ToggleCompleteCommand(id, GetUserId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<CategoryDto>> LoadCategories(Guid? selectedCategoryId = null)
    {
        var result = await _mediator.Send(new GetAllCategoriesQuery(1, 100));
        var categories = result.Items.ToList();
        if (selectedCategoryId.HasValue && categories.All(category => category.Id != selectedCategoryId.Value))
        {
            var selected = await _mediator.Send(new GetCategoryByIdQuery(selectedCategoryId.Value), HttpContext.RequestAborted);
            if (selected != null) categories.Add(selected);
        }
        return categories;
    }
}
