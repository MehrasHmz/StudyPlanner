using MediatR;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;
using FluentValidation;
using FluentValidation.Results;

namespace StudyPlanner.Application.Commands.StudyItems;
public class CreateStudyItemHandler : IRequestHandler<CreateStudyItemCommand, Guid>
{
    private readonly IStudyItemRepository _repo;
    private readonly ICategoryRepository _categories;
    public CreateStudyItemHandler(IStudyItemRepository repo, ICategoryRepository categories) { _repo = repo; _categories = categories; }
    public async Task<Guid> Handle(CreateStudyItemCommand r, CancellationToken ct)
    {
        await StudyItemCategoryAccess.VerifyAsync(_categories, r.CategoryId, r.UserId, null, ct);
        var item = new StudyItem(r.Title, r.Description ?? string.Empty, r.Type, r.Difficulty, r.EstimatedDurationMinutes,
            r.Priority, r.CategoryId, r.UserId, r.NextReviewDate);
        await _repo.AddAsync(item, ct);
        await _repo.SaveChangesAsync(ct);
        return item.Id;
    }
}
public class UpdateStudyItemHandler : IRequestHandler<UpdateStudyItemCommand>
{
    private readonly IStudyItemRepository _repo;
    private readonly ICategoryRepository _categories;
    public UpdateStudyItemHandler(IStudyItemRepository repo, ICategoryRepository categories) { _repo = repo; _categories = categories; }
    public async Task Handle(UpdateStudyItemCommand r, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(r.Id, ct);
        if (item == null || r.UserId == Guid.Empty || item.UserId != r.UserId || item.IsDeleted)
            throw new KeyNotFoundException("Study item not found.");
        await StudyItemCategoryAccess.VerifyAsync(_categories, r.CategoryId, r.UserId, item.CategoryId, ct);
        item.SetTitle(r.Title);
        item.SetDescription(r.Description ?? string.Empty);
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
internal static class StudyItemCategoryAccess
{
    public static async Task VerifyAsync(ICategoryRepository categories, Guid categoryId, Guid userId, Guid? existingCategoryId, CancellationToken ct)
    {
        var category = await categories.GetByIdAsync(categoryId, ct);
        // Keeping an archived category on an existing owned item must remain possible.
        if (category == null && categoryId == existingCategoryId) return;
        if (category == null || (category.UserId.HasValue && category.UserId != userId) ||
            (category.IsDeleted && categoryId != existingCategoryId))
            throw new ValidationException(new[] { new ValidationFailure("CategoryId", "Choose an available category from your library.") });
    }
}
public class DeleteStudyItemHandler : IRequestHandler<DeleteStudyItemCommand>
{
    private readonly IStudyItemRepository _repo;
    public DeleteStudyItemHandler(IStudyItemRepository repo) => _repo = repo;
    public async Task Handle(DeleteStudyItemCommand r, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(r.Id, ct);
        if (item == null || r.UserId == Guid.Empty || item.UserId != r.UserId || item.IsDeleted)
            throw new KeyNotFoundException("Study item not found.");
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
        var item = await _repo.GetByIdAsync(r.Id, ct);
        if (item == null || r.UserId == Guid.Empty || item.UserId != r.UserId || item.IsDeleted)
            throw new KeyNotFoundException("Study item not found.");
        if (item.IsCompleted) item.MarkIncomplete(); else item.MarkCompleted();
        item.Touch();
        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);
    }
}
