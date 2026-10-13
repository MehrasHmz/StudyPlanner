using System.Linq.Expressions;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Domain.Services;

public static class StudyReviewPolicy
{
    public static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static bool IsDue(DateTime? nextReviewDate, DateTime now) =>
        nextReviewDate.HasValue && AsUtc(nextReviewDate.Value) <= AsUtc(now);

    public static DateTime NextReviewAfter(DateTime completedAt) => AsUtc(completedAt).AddDays(7);

    // Keep the same boundary in memory and in PostgreSQL without client-side filtering.
    public static Expression<Func<StudyItem, bool>> DueAt(DateTime now)
    {
        var utcNow = AsUtc(now);
        return item => !item.IsDeleted && item.NextReviewDate.HasValue && item.NextReviewDate <= utcNow;
    }
}
