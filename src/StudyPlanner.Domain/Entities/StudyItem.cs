using StudyPlanner.Domain.Common;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Domain.Entities;
public class StudyItem : AuditableEntity
{
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public StudyItemType Type { get; private set; }
    public DifficultyLevel Difficulty { get; private set; }
    public int EstimatedDurationMinutes { get; private set; }
    public int Priority { get; private set; }
    public DateTime? NextReviewDate { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsDeleted { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid UserId { get; private set; }
    public Category Category { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private StudyItem() : base() { }
    public StudyItem(string title, string description, StudyItemType type, DifficultyLevel difficulty,
        int estimatedDurationMinutes, int priority, Guid categoryId, Guid userId, DateTime? nextReviewDate = null) : base()
    {
        SetTitle(title);
        Description = description?.Trim() ?? string.Empty;
        Type = type; Difficulty = difficulty;
        SetEstimatedDurationMinutes(estimatedDurationMinutes);
        SetPriority(priority);
        CategoryId = categoryId; UserId = userId;
        NextReviewDate = ToUtc(nextReviewDate);
    }
    public void SetTitle(string t) { if (string.IsNullOrWhiteSpace(t)) throw new ArgumentException("Title is required."); Title = t.Trim(); }
    public void SetDescription(string d) => Description = d?.Trim() ?? string.Empty;
    public void SetEstimatedDurationMinutes(int m) { if (m <= 0) throw new ArgumentOutOfRangeException(nameof(m), "Duration must be positive."); EstimatedDurationMinutes = m; }
    public void SetPriority(int p) { if (p < 1 || p > 5) throw new ArgumentOutOfRangeException(nameof(p), "Priority must be 1-5."); Priority = p; }
    public void UpdateType(StudyItemType t) => Type = t;
    public void UpdateDifficulty(DifficultyLevel d) => Difficulty = d;
    public void UpdateCategory(Guid c) => CategoryId = c;
    public void SetNextReviewDate(DateTime? d) => NextReviewDate = ToUtc(d);
    public void MarkCompleted() { IsCompleted = true; NextReviewDate = null; }
    public void MarkIncomplete() { IsCompleted = false; NextReviewDate = DateTime.UtcNow; }
    public void SoftDelete() => IsDeleted = true;
    public void Restore() => IsDeleted = false;
    public bool IsDueForReview(DateTime now) => NextReviewDate.HasValue && NextReviewDate.Value <= now;
    public int GetDifficultyWeight() => Difficulty switch { DifficultyLevel.Beginner => 1, DifficultyLevel.Intermediate => 2, DifficultyLevel.Advanced => 3, _ => 1 };

    private static DateTime? ToUtc(DateTime? d) => d.HasValue ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc) : null;
}
