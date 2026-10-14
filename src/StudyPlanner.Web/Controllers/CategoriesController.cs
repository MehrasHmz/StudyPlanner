using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Categories;
using StudyPlanner.Application.Authorization;
using StudyPlanner.Web.Security;

namespace StudyPlanner.Web.Controllers;
[Authorize]
public class CategoriesController : BaseController
{
    private readonly IMediator _mediator;
    public CategoriesController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index(int page = 1)
    {
        var result = await _mediator.Send(new GetAllCategoriesQuery(Math.Max(1, page), 12), HttpContext.RequestAborted);
        return View(result);
    }

    [Authorize(Policy = CategoryManagementRequirement.Policy)]
    public IActionResult Create()
    {
        ViewBag.Errors = new List<string>();
        return View();
    }

    [Authorize(Policy = CategoryManagementRequirement.Policy)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var cat = await _mediator.Send(new GetCategoryByIdQuery(id), HttpContext.RequestAborted);
        if (cat == null) return NotFound();
        ViewBag.Errors = new List<string>();
        return View(cat);
    }

    [HttpPost, Authorize(Policy = CategoryManagementRequirement.Policy)]
    public async Task<IActionResult> Create(CreateCategoryCommand cmd)
    {
        if (!ModelState.IsValid) return View(cmd);
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Category created!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            foreach (var error in ex.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return View(cmd);
        }
        catch (CategoryManagementDeniedException) { return Forbid(); }
        catch (Exception ex)
        {
            HttpContext.RequestServices?.GetService<ILogger<CategoriesController>>()?.LogError(ex, "Category creation failed.");
            ViewBag.Errors = new List<string> { "Unable to save the category. Please try again." };
            return View(cmd);
        }
    }

    [HttpPost, Authorize(Policy = CategoryManagementRequirement.Policy)]
    public async Task<IActionResult> Edit(UpdateCategoryCommand cmd)
    {
        var existing = await _mediator.Send(new GetCategoryByIdQuery(cmd.Id), HttpContext.RequestAborted);
        if (existing == null) return NotFound();
        if (!ModelState.IsValid) return View(existing);
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Category updated!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            foreach (var error in ex.Errors) ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return View(existing);
        }
        catch (CategoryManagementDeniedException) { return Forbid(); }
        catch (Exception ex)
        {
            HttpContext.RequestServices?.GetService<ILogger<CategoriesController>>()?.LogError(ex, "Category update failed.");
            ViewBag.Errors = new List<string> { "Unable to save the category. Please try again." };
            return View(existing);
        }
    }

    [HttpPost, Authorize(Policy = CategoryManagementRequirement.Policy)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try { await _mediator.Send(new DeleteCategoryCommand(id)); }
        catch (CategoryManagementDeniedException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException)
        {
            TempData["Error"] = "This category is referenced by study items and cannot be deleted.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "Category deleted.";
        return RedirectToAction(nameof(Index));
    }
}
