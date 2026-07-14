using StudyPlanner.Domain.Common;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Domain.Interfaces;
public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
}
public interface ICategoryRepository : IRepository<Category>
{
    Task<Category?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetPagedAsync(int page, int size, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
}
public interface IStudyItemRepository : IRepository<StudyItem>
{
    Task<IReadOnlyList<StudyItem>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<StudyItem>> GetDueForReviewAsync(Guid userId, DateTime now, CancellationToken ct = default);
    Task<StudyItem?> GetWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<StudyItem> Items, int Total)> GetPagedAsync(Guid userId, int page, int size, string? search, string? sortBy, bool desc, CancellationToken ct = default);
}
public interface IStudySessionRepository : IRepository<StudySession>
{
    Task<IReadOnlyList<StudySession>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<StudySession?> GetWithItemsAsync(Guid id, CancellationToken ct = default);
}
public interface IProgressRecordRepository : IRepository<ProgressRecord>
{
    Task<IReadOnlyList<ProgressRecord>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<int> GetTotalMinutesAsync(Guid userId, CancellationToken ct = default);
}
public interface IStudySessionPlanner
{
    IReadOnlyList<StudyItem> SelectItems(IReadOnlyList<StudyItem> available, int minutes, DateTime now);
}
