namespace StudyPlanner.Domain.Services;

public static class StudyProgressPolicy
{
    public const int MaximumMinutes = 480;
    public const int MaximumNotesLength = 1000;
    public static bool IsValidScore(double? score) => !score.HasValue ||
        (double.IsFinite(score.Value) && score.Value >= 0 && score.Value <= 1);
}
