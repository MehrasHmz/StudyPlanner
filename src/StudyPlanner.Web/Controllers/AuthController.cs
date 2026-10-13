using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyPlanner.Application.Commands.Users;
using Microsoft.AspNetCore.RateLimiting;

namespace StudyPlanner.Web.Controllers;
public class AuthController : Controller
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    public IActionResult Login() => View();
    public IActionResult Register() => View();

    [HttpPost, EnableRateLimiting("auth-login")] public async Task<IActionResult> Login(LoginCommand cmd)
    {
        ViewData["Email"] = cmd.Email;
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please enter a valid email and password.";
            return View();
        }
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
        catch (ValidationException)
        {
            TempData["Error"] = "Please enter a valid email and password.";
            return View();
        }
        catch (UnauthorizedAccessException)
        {
            TempData["Error"] = "Invalid credentials.";
            return View();
        }
    }

    [HttpPost, EnableRateLimiting("auth-register")] public async Task<IActionResult> Register(RegisterCommand cmd)
    {
        ViewData["Email"] = cmd.Email;
        ViewData["FullName"] = cmd.FullName;
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please enter valid registration details.";
            return View();
        }
        try
        {
            await _mediator.Send(cmd);
            TempData["Success"] = "Account created successfully! Please sign in.";
            return RedirectToAction("Login");
        }
        catch (ValidationException)
        {
            TempData["Error"] = "Please enter valid registration details.";
            return View();
        }
        catch (InvalidOperationException)
        {
            TempData["Error"] = "Unable to create an account with these details.";
            return View();
        }
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
