using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Authorization;

public interface ICurrentUser { Guid? UserId { get; } }
public sealed class AnonymousCurrentUser : ICurrentUser { public Guid? UserId => null; }
public interface ICategoryPermission
{
    Task<bool> CanManageAsync(CancellationToken ct = default);
    Task RequireManagementAsync(CancellationToken ct = default);
}
public sealed class CategoryPermission : ICategoryPermission
{
    private readonly ICurrentUser _caller;
    private readonly IUserRepository _users;
    public CategoryPermission(ICurrentUser caller, IUserRepository users) { _caller = caller; _users = users; }
    public async Task<bool> CanManageAsync(CancellationToken ct = default)
    {
        if (_caller.UserId is not Guid id || id == Guid.Empty) return false;
        var user = await _users.GetByIdAsync(id, ct);
        return user is { IsActive: true, Role: UserRole.Administrator };
    }
    public async Task RequireManagementAsync(CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) throw new CategoryManagementDeniedException();
    }
}
public sealed class CategoryManagementDeniedException : UnauthorizedAccessException
{
    public CategoryManagementDeniedException() : base("Category management requires administrator permission.") { }
}
