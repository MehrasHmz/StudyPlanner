using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Categories;

namespace StudyPlanner.Web.Controllers;
public class CategoriesController : Controller
{
    private readonly IMediator _mediator;
    public CategoriesController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index(int page = 1)
    {
        var result = await _mediator.Send(new GetAllCategoriesQuery(page));
        return View(result);
    }

    public IActionResult Create()
    {
        ViewBag.Errors = new List<string>();
        return View();
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var cats = await _mediator.Send(new GetAllCategoriesQuery(1, 100));
        var cat = cats.Items.FirstOrDefault(c => c.Id == id);
        if (cat == null) return NotFound();
        ViewBag.Errors = new List<string>();
        return View(cat);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCategoryCommand cmd)
    {
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Category created!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
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
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Category updated!";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ViewBag.Errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            var cats = await _mediator.Send(new GetAllCategoriesQuery(1, 100));
            var cat = cats.Items.FirstOrDefault(c => c.Id == cmd.Id);
            return View(cat ?? new CategoryDto(cmd.Id, cmd.Name, cmd.Description));
        }
        catch (Exception ex)
        {
            ViewBag.Errors = new List<string> { ex.Message };
            var cats = await _mediator.Send(new GetAllCategoriesQuery(1, 100));
            var cat = cats.Items.FirstOrDefault(c => c.Id == cmd.Id);
            return View(cat);
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
