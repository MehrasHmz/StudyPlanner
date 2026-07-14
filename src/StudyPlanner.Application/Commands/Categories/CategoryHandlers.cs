using MediatR;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Commands.Categories;
public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _repo;
    public CreateCategoryHandler(ICategoryRepository repo) => _repo = repo;
    public async Task<Guid> Handle(CreateCategoryCommand r, CancellationToken ct)
    {
        var cat = new Category(r.Name, r.Description);
        await _repo.AddAsync(cat, ct);
        await _repo.SaveChangesAsync(ct);
        return cat.Id;
    }
}
public class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _repo;
    public UpdateCategoryHandler(ICategoryRepository repo) => _repo = repo;
    public async Task Handle(UpdateCategoryCommand r, CancellationToken ct)
    {
        var cat = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Category not found.");
        cat.SetName(r.Name);
        cat.SetDescription(r.Description);
        _repo.Update(cat);
        await _repo.SaveChangesAsync(ct);
    }
}
public class DeleteCategoryHandler : IRequestHandler<DeleteCategoryCommand>
{
    private readonly ICategoryRepository _repo;
    public DeleteCategoryHandler(ICategoryRepository repo) => _repo = repo;
    public async Task Handle(DeleteCategoryCommand r, CancellationToken ct)
    {
        var cat = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Category not found.");
        cat.SoftDelete();
        _repo.Update(cat);
        await _repo.SaveChangesAsync(ct);
    }
}
