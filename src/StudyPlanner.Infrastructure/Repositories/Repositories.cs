using Microsoft.EntityFrameworkCore;
using StudyPlanner.Domain.Common;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.Infrastructure.Repositories;
public class Repository<T> : IRepository<T> where T : Entity
{
    protected readonly AppDbContext _ctx;
    public Repository(AppDbContext ctx) => _ctx = ctx;
    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) => await _ctx.Set<T>().FindAsync(new object[] { id }, ct);
    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) => await _ctx.Set<T>().ToListAsync(ct);
    public virtual async Task<T> AddAsync(T entity, CancellationToken ct = default) { await _ctx.Set<T>().AddAsync(entity, ct); return entity; }
    public virtual void Update(T entity) => _ctx.Set<T>().Update(entity);
    public virtual void Delete(T entity) => _ctx.Set<T>().Remove(entity);
    public virtual async Task<int> SaveChangesAsync(CancellationToken ct = default) => await _ctx.SaveChangesAsync(ct);
}
public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext ctx) : base(ctx) { }
    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await _ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email.ToLower(), ct);
    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) =>
        await _ctx.Users.AnyAsync(u => u.Email == email.ToLower(), ct);
}
public class CategoryRepository : Repository<Category>, ICategoryRepository
{
    public CategoryRepository(AppDbContext ctx) : base(ctx) { }
    public async Task<Category?> GetByNameAsync(string name, CancellationToken ct = default) =>
        await _ctx.Categories.FirstOrDefaultAsync(c => c.Name == name, ct);
    public async Task<IReadOnlyList<Category>> GetPagedAsync(int page, int size, CancellationToken ct = default) =>
        await _ctx.Categories.AsNoTracking().OrderBy(c => c.Name).Skip((page - 1) * size).Take(size).ToListAsync(ct);
    public async Task<int> GetCountAsync(CancellationToken ct = default) => await _ctx.Categories.CountAsync(ct);
}
public class StudyItemRepository : Repository<StudyItem>, IStudyItemRepository
{
    public StudyItemRepository(AppDbContext ctx) : base(ctx) { }
    public async Task<IReadOnlyList<StudyItem>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _ctx.StudyItems.AsNoTracking().Include(s => s.Category).Where(s => s.UserId == userId).ToListAsync(ct);
    public async Task<IReadOnlyList<StudyItem>> GetDueForReviewAsync(Guid userId, DateTime now, CancellationToken ct = default) =>
        await _ctx.StudyItems.AsNoTracking().Include(s => s.Category)
            .Where(s => s.UserId == userId && !s.IsCompleted && s.NextReviewDate.HasValue && s.NextReviewDate <= now).ToListAsync(ct);
    public async Task<StudyItem?> GetWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.StudyItems.Include(s => s.Category).Include(s => s.User).FirstOrDefaultAsync(s => s.Id == id, ct);
    public async Task<(IReadOnlyList<StudyItem> Items, int Total)> GetPagedAsync(Guid userId, int page, int size, string? search, string? sortBy, bool desc, CancellationToken ct = default)
    {
        var q = _ctx.StudyItems.AsNoTracking().Include(s => s.Category).Where(s => s.UserId == userId).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.ToLower(); q = q.Where(x => x.Title.ToLower().Contains(s) || x.Description.ToLower().Contains(s)); }
        var total = await q.CountAsync(ct);
        q = sortBy?.ToLower() switch
        {
            "title" => desc ? q.OrderByDescending(x => x.Title) : q.OrderBy(x => x.Title),
            "priority" => desc ? q.OrderByDescending(x => x.Priority) : q.OrderBy(x => x.Priority),
            "difficulty" => desc ? q.OrderByDescending(x => x.Difficulty) : q.OrderBy(x => x.Difficulty),
            "created" => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
            _ => q.OrderByDescending(x => x.CreatedAt)
        };
        return (await q.Skip((page - 1) * size).Take(size).ToListAsync(ct), total);
    }
}
public class StudySessionRepository : Repository<StudySession>, IStudySessionRepository
{
    public StudySessionRepository(AppDbContext ctx) : base(ctx) { }
    public async Task<IReadOnlyList<StudySession>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _ctx.StudySessions.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.PlannedDate).ToListAsync(ct);
    public async Task<StudySession?> GetWithItemsAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.StudySessions.Include(s => s.SessionItems).ThenInclude(si => si.StudyItem).FirstOrDefaultAsync(s => s.Id == id, ct);
}
public class ProgressRecordRepository : Repository<ProgressRecord>, IProgressRecordRepository
{
    public ProgressRecordRepository(AppDbContext ctx) : base(ctx) { }
    public async Task<IReadOnlyList<ProgressRecord>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _ctx.ProgressRecords.AsNoTracking().Include(p => p.StudyItem).Where(p => p.UserId == userId).OrderByDescending(p => p.CompletedAt).ToListAsync(ct);
    public async Task<int> GetTotalMinutesAsync(Guid userId, CancellationToken ct = default) =>
        await _ctx.ProgressRecords.Where(p => p.UserId == userId).SumAsync(p => p.MinutesSpent, ct);
}
