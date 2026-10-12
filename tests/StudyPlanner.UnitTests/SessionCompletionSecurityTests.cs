using FluentAssertions;
using Moq;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.Validators;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.UnitTests;

public class SessionCompletionSecurityTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Mock<IStudySessionRepository> _sessions = new();
    private readonly Mock<IStudyItemRepository> _items = new();
    private readonly Mock<IProgressRecordRepository> _progress = new();
    private readonly List<ProgressRecord> _addedRecords = new();
    private readonly StudySession _session;
    private readonly StudyItem _first;
    private readonly StudyItem _second;

    public SessionCompletionSecurityTests()
    {
        _session = new StudySession(_owner, DateTime.UtcNow, 60);
        _first = CreateItem(_owner);
        _second = CreateItem(_owner);
        AddMember(_first, 1);
        AddMember(_second, 2);
        _sessions.Setup(r => r.GetWithItemsAsync(_session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_session);
        _progress.Setup(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ProgressRecord, CancellationToken>((record, _) => _addedRecords.Add(record))
            .ReturnsAsync((ProgressRecord record, CancellationToken _) => record);
    }

    [Theory]
    [InlineData("foreign-user")]
    [InlineData("empty-user")]
    [InlineData("missing-session")]
    public async Task InvalidSessionOwnership_RejectsBeforeAnyWrites(string reason)
    {
        var command = Command();
        command = reason switch
        {
            "foreign-user" => command with { UserId = Guid.NewGuid() },
            "empty-user" => command with { UserId = Guid.Empty },
            _ => command with { SessionId = Guid.NewGuid() }
        };

        var act = () => Handler().Handle(command, default);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("Session not found.");
        AssertNoChanges();
        _items.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("same-user-unrelated")]
    [InlineData("foreign-unrelated")]
    [InlineData("foreign-member")]
    [InlineData("missing-member")]
    [InlineData("deleted-member")]
    public async Task InvalidSecondItem_RejectsEntireRequestWithoutChangingValidFirstItem(string reason)
    {
        var invalid = CreateItem(reason.StartsWith("foreign") ? Guid.NewGuid() : _owner);
        if (reason.EndsWith("member")) AddMember(invalid, 3);
        if (reason == "missing-member")
            _items.Setup(r => r.GetByIdAsync(invalid.Id, It.IsAny<CancellationToken>())).ReturnsAsync((StudyItem?)null);
        if (reason == "deleted-member") invalid.SoftDelete();
        var command = Command() with { Results = new() { Result(_first), Result(invalid) } };

        var act = () => Handler().Handle(command, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Invalid session results.");
        AssertNoChanges();
        invalid.IsCompleted.Should().BeFalse();
        invalid.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task DuplicateResults_RejectWithoutProgressOrStateChanges()
    {
        var command = Command() with { Results = new() { Result(_first), Result(_first) } };

        var act = () => Handler().Handle(command, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertNoChanges();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null-list")]
    [InlineData("null-result")]
    [InlineData("empty-item")]
    [InlineData("zero-minutes")]
    [InlineData("negative-minutes")]
    public async Task MalformedResults_RejectWithoutChanges(string reason)
    {
        var results = reason switch
        {
            "empty" => new List<ItemResultDto>(),
            "null-list" => null!,
            "null-result" => new List<ItemResultDto> { Result(_first), null! },
            "empty-item" => new List<ItemResultDto> { Result(_first), new(Guid.Empty, 20, null, "") },
            "zero-minutes" => new List<ItemResultDto> { Result(_first), Result(_second) with { MinutesSpent = 0 } },
            _ => new List<ItemResultDto> { Result(_first), Result(_second) with { MinutesSpent = -1 } }
        };
        var command = Command() with { Results = results };

        var act = () => Handler().Handle(command, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertNoChanges();
        var validation = new CompleteSessionValidator().Validate(command);
        validation.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionStatus.InProgress)]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Cancelled)]
    public async Task InvalidSessionState_RejectsWithoutWrites(SessionStatus status)
    {
        if (status == SessionStatus.Cancelled) _session.CancelSession();
        else
        {
            _session.StartSession();
            if (status == SessionStatus.Completed) _session.CompleteSession();
        }

        var act = () => Handler().Handle(Command(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertNoChanges(status);
    }

    [Fact]
    public async Task ValidCompletion_UpdatesMembersAndSavesProgressOnce()
    {
        var command = Command();
        var before = DateTime.UtcNow;

        await Handler().Handle(command, default);

        _session.Status.Should().Be(SessionStatus.Completed);
        _first.IsCompleted.Should().BeTrue();
        _second.IsCompleted.Should().BeTrue();
        _first.NextReviewDate.Should().BeOnOrAfter(before.AddDays(7));
        _addedRecords.Should().HaveCount(2);
        _addedRecords.Select(r => r.StudyItemId).Should().BeEquivalentTo(new[] { _first.Id, _second.Id });
        _addedRecords.Should().OnlyContain(r => r.UserId == _owner && r.SessionId == _session.Id && r.MinutesSpent == 20);
        _addedRecords.Should().OnlyContain(r => r.ResultScore == 0.8 && r.Notes == "Reviewed");
        _items.Verify(r => r.Update(_first), Times.Once);
        _items.Verify(r => r.Update(_second), Times.Once);
        _sessions.Verify(r => r.Update(_session), Times.Once);
        _sessions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _progress.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidSubset_PreservesExistingPartialResultBehavior()
    {
        await Handler().Handle(Command() with { Results = new() { Result(_first) } }, default);

        _session.Status.Should().Be(SessionStatus.Completed);
        _first.IsCompleted.Should().BeTrue();
        _second.IsCompleted.Should().BeFalse();
        _addedRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task RepeatedCompletion_DoesNotAddMoreProgress()
    {
        await Handler().Handle(Command(), default);
        var act = () => Handler().Handle(Command(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();

        _addedRecords.Should().HaveCount(2);
        _sessions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private CompleteSessionHandler Handler() => new(_sessions.Object, _items.Object, _progress.Object);
    private CompleteSessionCommand Command() => new(_session.Id, _owner, new() { Result(_first), Result(_second) });
    private static ItemResultDto Result(StudyItem item) => new(item.Id, 20, 0.8, "Reviewed");
    private static StudyItem CreateItem(Guid owner) => new("Item", "", StudyItemType.Programming,
        DifficultyLevel.Beginner, 20, 3, Guid.NewGuid(), owner);

    private void AddMember(StudyItem item, int order)
    {
        _session.AddItem(new StudySessionItem(_session.Id, item.Id, 20, order));
        _items.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
    }

    private void AssertNoChanges(SessionStatus expectedStatus = SessionStatus.Planned)
    {
        _session.Status.Should().Be(expectedStatus);
        _first.IsCompleted.Should().BeFalse();
        _second.IsCompleted.Should().BeFalse();
        _first.NextReviewDate.Should().BeNull();
        _second.NextReviewDate.Should().BeNull();
        _addedRecords.Should().BeEmpty();
        _progress.Verify(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _items.Verify(r => r.Update(It.IsAny<StudyItem>()), Times.Never);
        _sessions.Verify(r => r.Update(It.IsAny<StudySession>()), Times.Never);
        _sessions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _items.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _progress.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
