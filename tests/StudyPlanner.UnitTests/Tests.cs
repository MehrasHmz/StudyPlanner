using FluentAssertions;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Services;

namespace StudyPlanner.UnitTests;
public class StudyItemTests
{
    [Fact] public void Create_WithZeroDuration_ShouldThrow()
    {
        var act = () => new StudyItem("T", "D", StudyItemType.Programming, DifficultyLevel.Beginner, 0, 3, Guid.NewGuid(), Guid.NewGuid());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    [Fact] public void Create_WithNegativeDuration_ShouldThrow()
    {
        var act = () => new StudyItem("T", "D", StudyItemType.Programming, DifficultyLevel.Beginner, -5, 3, Guid.NewGuid(), Guid.NewGuid());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    [Fact] public void SetPriority_InvalidRange_ShouldThrow()
    {
        var item = new StudyItem("T", "D", StudyItemType.Programming, DifficultyLevel.Beginner, 30, 3, Guid.NewGuid(), Guid.NewGuid());
        var act = () => item.SetPriority(6);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    [Fact] public void MarkCompleted_ShouldSetCompleted()
    {
        var item = new StudyItem("T", "D", StudyItemType.Programming, DifficultyLevel.Beginner, 30, 3, Guid.NewGuid(), Guid.NewGuid());
        item.MarkCompleted();
        item.IsCompleted.Should().BeTrue();
        item.NextReviewDate.Should().BeNull();
    }
    [Fact] public void IsDueForReview_PastDate_ShouldReturnTrue()
    {
        var item = new StudyItem("T", "D", StudyItemType.Programming, DifficultyLevel.Beginner, 30, 3, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-1));
        item.IsDueForReview(DateTime.UtcNow).Should().BeTrue();
    }
    [Fact] public void IsDueForReview_FutureDate_ShouldReturnFalse()
    {
        var item = new StudyItem("T", "D", StudyItemType.Programming, DifficultyLevel.Beginner, 30, 3, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1));
        item.IsDueForReview(DateTime.UtcNow).Should().BeFalse();
    }
}
public class StudySessionTests
{
    [Fact] public void StartSession_WhenPlanned_ShouldSetInProgress()
    {
        var s = new StudySession(Guid.NewGuid(), DateTime.UtcNow, 60);
        s.StartSession();
        s.Status.Should().Be(SessionStatus.InProgress);
    }
    [Fact] public void CompleteSession_WhenNotInProgress_ShouldThrow()
    {
        var s = new StudySession(Guid.NewGuid(), DateTime.UtcNow, 60);
        var act = () => s.CompleteSession();
        act.Should().Throw<InvalidOperationException>();
    }
    [Fact] public void HasTimeFor_ShouldWork()
    {
        var s = new StudySession(Guid.NewGuid(), DateTime.UtcNow, 60);
        s.AddItem(new StudySessionItem(s.Id, Guid.NewGuid(), 30, 1));
        s.HasTimeFor(30).Should().BeTrue();
        s.HasTimeFor(31).Should().BeFalse();
    }
}
public class PlannerTests
{
    private readonly StudySessionPlanner _planner = new();
    [Fact] public void SelectItems_EmptyList_ShouldReturnEmpty()
    {
        _planner.SelectItems(Array.Empty<StudyItem>(), 60, DateTime.UtcNow).Should().BeEmpty();
    }
    [Fact] public void SelectItems_ShouldNotExceedTime()
    {
        var items = new List<StudyItem>
        {
            new("A", "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 4, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-1)),
            new("B", "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, Guid.NewGuid(), Guid.NewGuid()),
            new("C", "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 2, Guid.NewGuid(), Guid.NewGuid()),
        };
        var result = _planner.SelectItems(items, 35, DateTime.UtcNow);
        result.Sum(i => i.EstimatedDurationMinutes).Should().BeLessThanOrEqualTo(35);
    }
    [Fact] public void SelectItems_ShouldExcludeCompleted()
    {
        var item = new StudyItem("A", "", StudyItemType.Programming, DifficultyLevel.Beginner, 10, 5, Guid.NewGuid(), Guid.NewGuid());
        item.MarkCompleted();
        var result = _planner.SelectItems(new[] { item }, 60, DateTime.UtcNow);
        result.Should().BeEmpty();
    }
    [Fact] public void SelectItems_HigherPriorityShouldComeFirst()
    {
        var items = new List<StudyItem>
        {
            new("Low", "", StudyItemType.Programming, DifficultyLevel.Beginner, 10, 1, Guid.NewGuid(), Guid.NewGuid()),
            new("High", "", StudyItemType.Programming, DifficultyLevel.Beginner, 10, 5, Guid.NewGuid(), Guid.NewGuid()),
        };
        var result = _planner.SelectItems(items, 20, DateTime.UtcNow);
        result.First().Title.Should().Be("High");
    }
}
