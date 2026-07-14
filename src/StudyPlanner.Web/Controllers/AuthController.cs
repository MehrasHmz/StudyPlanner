using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Users;

namespace StudyPlanner.Web.Controllers;
public class AuthController : Controller
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    public IActionResult Login() => View();
    public IActionResult Register() => View();

    [HttpPost] public async Task<IActionResult> Login(LoginCommand cmd)
    {
        try
        {
            var result = await _mediator.Send(cmd);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.User.Id.ToString()),
                new(ClaimTypes.Name, result.User.FullName),
                new(ClaimTypes.Email, result.User.Email)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            TempData["Success"] = $"Welcome back, {result.User.FullName}!";
            return RedirectToAction("Index", "Home");
        }
        catch (UnauthorizedAccessException ex)
        {
            TempData["Error"] = ex.Message;
            return View();
        }
    }

    [HttpPost] public async Task<IActionResult> Register(RegisterCommand cmd)
    {
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Account created successfully! Please sign in.";
            return RedirectToAction("Login");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return View();
        }
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
