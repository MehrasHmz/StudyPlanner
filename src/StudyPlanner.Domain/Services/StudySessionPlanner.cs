using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Domain.Services;
public class StudySessionPlanner : IStudySessionPlanner
{
    public IReadOnlyList<StudyItem> SelectItems(IReadOnlyList<StudyItem> available, int minutes, DateTime now)
    {
        if (available == null || available.Count == 0 || minutes <= 0) return Array.Empty<StudyItem>();

        var candidates = available.Where(i => !i.IsCompleted && i.EstimatedDurationMinutes <= minutes).ToList();
        if (candidates.Count == 0) return Array.Empty<StudyItem>();

        var scored = candidates.Select(i => new { Item = i, Score = CalculateScore(i, now) })
            .OrderByDescending(x => x.Score).ToList();

        var result = new List<StudyItem>();
        var remaining = minutes;
        foreach (var s in scored)
        {
            if (s.Item.EstimatedDurationMinutes <= remaining)
            {
                result.Add(s.Item);
                remaining -= s.Item.EstimatedDurationMinutes;
            }
        }
        return result.AsReadOnly();
    }

    private double CalculateScore(StudyItem item, DateTime now)
    {
        double score = item.Priority * 20.0;
        score += item.GetDifficultyWeight() * 10.0;

        if (item.NextReviewDate.HasValue)
        {
            var overdue = (now - item.NextReviewDate.Value).TotalDays;
            if (overdue >= 14) score += 200;
            else if (overdue >= 7) score += 150;
            else if (overdue >= 3) score += 100;
            else if (overdue >= 1) score += 60;
            else if (overdue >= 0) score += 40;
            else score += Math.Max(0, 20 + (overdue * 5));
        }
        return score;
    }
}
