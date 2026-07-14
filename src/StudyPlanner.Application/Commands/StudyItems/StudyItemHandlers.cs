using MediatR;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Commands.StudyItems;
public class CreateStudyItemHandler : IRequestHandler<CreateStudyItemCommand, Guid>
{
    private readonly IStudyItemRepository _repo;
    public CreateStudyItemHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task<Guid> Handle(CreateStudyItemCommand r, CancellationToken ct)
    {
        var item = new StudyItem(r.Title, r.Description, r.Type, r.Difficulty, r.EstimatedDurationMinutes,
            r.Priority, r.CategoryId, r.UserId, r.NextReviewDate);
        await _repo.AddAsync(item, ct);
        await _repo.SaveChangesAsync(ct);
        return item.Id;
    }
}
public class UpdateStudyItemHandler : IRequestHandler<UpdateStudyItemCommand>
{
    private readonly IStudyItemRepository _repo;
    public UpdateStudyItemHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task Handle(UpdateStudyItemCommand r, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Study item not found.");
        item.SetTitle(r.Title);
        item.SetDescription(r.Description);
        item.UpdateType(r.Type);
        item.UpdateDifficulty(r.Difficulty);
        item.SetEstimatedDurationMinutes(r.EstimatedDurationMinutes);
        item.SetPriority(r.Priority);
        item.UpdateCategory(r.CategoryId);
        item.SetNextReviewDate(r.NextReviewDate);
        item.Touch();
        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);
    }
}
public class DeleteStudyItemHandler : IRequestHandler<DeleteStudyItemCommand>
{
    private readonly IStudyItemRepository _repo;
    public DeleteStudyItemHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task Handle(DeleteStudyItemCommand r, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Study item not found.");
        item.SoftDelete();
        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);
    }
}
public class ToggleCompleteHandler : IRequestHandler<ToggleCompleteCommand>
{
    private readonly IStudyItemRepository _repo;
    public ToggleCompleteHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task Handle(ToggleCompleteCommand r, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(r.Id, ct) ?? throw new KeyNotFoundException("Study item not found.");
        if (item.IsCompleted) item.MarkIncomplete(); else item.MarkCompleted();
        item.Touch();
        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);
    }
}
