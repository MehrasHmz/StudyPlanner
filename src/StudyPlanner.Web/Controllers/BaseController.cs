using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace StudyPlanner.Web.Controllers;
public abstract class BaseController : Controller
{
    protected Guid GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("User not authenticated.");
        return userId;
    }
}
