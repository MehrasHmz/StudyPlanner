using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using StudyPlanner.Application.Authorization;

namespace StudyPlanner.Web.Security;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public HttpCurrentUser(IHttpContextAccessor http) => _http = http;
    public Guid? UserId => _http.HttpContext?.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(_http.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
public sealed class CategoryManagementRequirement : IAuthorizationRequirement
{
    public const string Policy = "ManageSharedCategories";
}
public sealed class CategoryManagementAuthorization : AuthorizationHandler<CategoryManagementRequirement>
{
    private readonly ICategoryPermission _permission;
    public CategoryManagementAuthorization(ICategoryPermission permission) => _permission = permission;
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CategoryManagementRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && await _permission.CanManageAsync()) context.Succeed(requirement);
    }
}
