using MediatR;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Application.Authorization;
using FluentValidation;
using FluentValidation.Results;

namespace StudyPlanner.Application.Commands.Categories;
public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _repo;
    private readonly ICategoryPermission _permission;
    public CreateCategoryHandler(ICategoryRepository repo, ICategoryPermission permission) { _repo = repo; _permission = permission; }
    public async Task<Guid> Handle(CreateCategoryCommand r, CancellationToken ct)
    {
        Guid? owner = await _permission.RequirePersonalManagementAsync(ct);
        if (r.IsShared) { await _permission.RequireManagementAsync(ct); owner = null; }
        if (await _repo.GetActiveByNameAsync(r.Name, owner, ct) != null)
            throw new ValidationException(new[] { new ValidationFailure("Name", "A category with this name already exists.") });
        var cat = new Category(r.Name, r.Description ?? string.Empty, owner);
        await _repo.AddAsync(cat, ct);
        await _repo.SaveChangesAsync(ct);
        return cat.Id;
    }
}
public class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _repo;
    private readonly ICategoryPermission _permission;
    public UpdateCategoryHandler(ICategoryRepository repo, ICategoryPermission permission) { _repo = repo; _permission = permission; }
    public async Task Handle(UpdateCategoryCommand r, CancellationToken ct)
    {
        await _permission.RequirePersonalManagementAsync(ct);
        var cat = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Category not found.");
        await _permission.RequireManagementAsync(cat, ct);
        if (cat.IsDeleted) throw new KeyNotFoundException("Category not found.");
        var duplicate = await _repo.GetActiveByNameAsync(r.Name, cat.UserId, ct);
        if (duplicate != null && duplicate.Id != cat.Id)
            throw new ValidationException(new[] { new ValidationFailure("Name", "A category with this name already exists.") });
        cat.SetName(r.Name);
        cat.SetDescription(r.Description ?? string.Empty);
        _repo.Update(cat);
        await _repo.SaveChangesAsync(ct);
    }
}
public class DeleteCategoryHandler : IRequestHandler<DeleteCategoryCommand>
{
    private readonly ICategoryRepository _repo;
    private readonly ICategoryPermission _permission;
    public DeleteCategoryHandler(ICategoryRepository repo, ICategoryPermission permission) { _repo = repo; _permission = permission; }
    public async Task Handle(DeleteCategoryCommand r, CancellationToken ct)
    {
        await _permission.RequirePersonalManagementAsync(ct);
        var cat = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Category not found.");
        await _permission.RequireManagementAsync(cat, ct);
        if (cat.IsDeleted) throw new KeyNotFoundException("Category not found.");
        if (cat.UserId == null && await _repo.HasStudyItemsAsync(cat.Id, ct))
            throw new InvalidOperationException("This category is referenced by study items and cannot be deleted.");
        cat.SoftDelete();
        _repo.Update(cat);
        await _repo.SaveChangesAsync(ct);
    }
}
