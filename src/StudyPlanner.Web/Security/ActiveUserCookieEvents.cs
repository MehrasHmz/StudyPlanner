using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using StudyPlanner.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace StudyPlanner.Web.Security;

public sealed class ActiveUserCookieEvents : CookieAuthenticationEvents
{
    private readonly IUserRepository _users;
    public ActiveUserCookieEvents(IUserRepository users) => _users = users;
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // Public probes must remain available independently of an incoming stale cookie.
        if (context.HttpContext.Request.Path.StartsWithSegments("/health")) return;
        var claim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = Guid.TryParse(claim, out var id) ? await _users.GetByIdAsync(id, context.HttpContext.RequestAborted) : null;
        if (user?.IsActive == true && Enum.IsDefined(user.Role)) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        return new ViewResult { ViewName = "~/Views/Auth/AccessDenied.cshtml", StatusCode = StatusCodes.Status403Forbidden }
            .ExecuteResultAsync(new ActionContext(context.HttpContext, context.HttpContext.GetRouteData(), new ActionDescriptor()));
    }
}
