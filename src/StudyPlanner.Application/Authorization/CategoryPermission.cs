using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Application.Authorization;

public interface ICurrentUser { Guid? UserId { get; } }
public sealed class AnonymousCurrentUser : ICurrentUser { public Guid? UserId => null; }
public interface ICategoryPermission
{
    Task<bool> CanManageAsync(CancellationToken ct = default);
    Task RequireManagementAsync(CancellationToken ct = default);
    Task<Guid> RequirePersonalManagementAsync(CancellationToken ct = default);
    Task RequireManagementAsync(Category category, CancellationToken ct = default);
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
    public async Task<Guid> RequirePersonalManagementAsync(CancellationToken ct = default)
    {
        if (_caller.UserId is Guid id && id != Guid.Empty)
        {
            var user = await _users.GetByIdAsync(id, ct);
            if (user is { IsActive: true } && user.Role is UserRole.Regular or UserRole.Administrator) return id;
        }
        throw new CategoryManagementDeniedException();
    }
    public async Task RequireManagementAsync(Category category, CancellationToken ct = default)
    {
        var userId = await RequirePersonalManagementAsync(ct);
        if (category.UserId is Guid owner)
        {
            if (owner != userId) throw new KeyNotFoundException("Category not found.");
        }
        else await RequireManagementAsync(ct);
    }
}
public sealed class CategoryManagementDeniedException : UnauthorizedAccessException
{
    public CategoryManagementDeniedException() : base("You do not have permission to manage this category.") { }
}
