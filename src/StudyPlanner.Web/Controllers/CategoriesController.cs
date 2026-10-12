using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Categories;

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

    public IActionResult Create()
    {
        ViewBag.Errors = new List<string>();
        return View();
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var cat = await _mediator.Send(new GetCategoryByIdQuery(id), HttpContext.RequestAborted);
        if (cat == null) return NotFound();
        ViewBag.Errors = new List<string>();
        return View(cat);
    }

    [HttpPost]
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
        catch (Exception ex)
        {
            ViewBag.Errors = new List<string> { ex.Message };
            return View(cmd);
        }
    }

    [HttpPost]
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
        catch (Exception ex)
        {
            ViewBag.Errors = new List<string> { ex.Message };
            return View(existing);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteCategoryCommand(id));
        TempData["Success"] = "Category deleted.";
        return RedirectToAction(nameof(Index));
    }
}
