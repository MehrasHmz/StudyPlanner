using FluentAssertions;
using Moq;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Application.Queries.StudyItems;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.UnitTests;

public class StudyItemOwnershipTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Mock<IStudyItemRepository> _repo = new();

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("toggle")]
    public async Task Owner_CanMutateOwnItem(string operation)
    {
        var item = CreateItem();
        SetupItem(item);

        await Mutate(operation, item.Id, _owner);

        if (operation == "edit") item.Title.Should().Be("Updated title");
        if (operation == "delete") item.IsDeleted.Should().BeTrue();
        if (operation == "toggle") item.IsCompleted.Should().BeTrue();
        _repo.Verify(r => r.Update(item), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("edit", "foreign")]
    [InlineData("delete", "foreign")]
    [InlineData("toggle", "foreign")]
    [InlineData("edit", "missing")]
    [InlineData("delete", "missing")]
    [InlineData("toggle", "missing")]
    [InlineData("edit", "deleted")]
    [InlineData("delete", "deleted")]
    [InlineData("toggle", "deleted")]
    [InlineData("edit", "empty-user")]
    [InlineData("delete", "empty-user")]
    [InlineData("toggle", "empty-user")]
    public async Task InvalidOwnershipOrResource_RejectsWithoutMutation(string operation, string reason)
    {
        var item = CreateItem();
        if (reason == "deleted") item.SoftDelete();
        if (reason != "missing") SetupItem(item);
        var userId = reason == "foreign" ? Guid.NewGuid() : reason == "empty-user" ? Guid.Empty : _owner;

        var act = () => Mutate(operation, item.Id, userId);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("Study item not found.");
        item.Title.Should().Be("Original title");
        item.IsCompleted.Should().BeFalse();
        item.IsDeleted.Should().Be(reason == "deleted");
        item.NextReviewDate.Should().BeNull();
        item.UpdatedAt.Should().BeNull();
        _repo.Verify(r => r.Update(It.IsAny<StudyItem>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Owner_CanToggleCompletedItemBackToIncomplete()
    {
        var item = CreateItem();
        item.MarkCompleted();
        SetupItem(item);

        await Mutate("toggle", item.Id, _owner);

        item.IsCompleted.Should().BeFalse();
        item.NextReviewDate.Should().NotBeNull();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetailQuery_OnlyReturnsOwnersItem(bool ownsItem)
    {
        var item = CreateItem();
        _repo.Setup(r => r.GetWithDetailsAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await new GetStudyItemByIdHandler(_repo.Object)
            .Handle(new GetStudyItemByIdQuery(item.Id, ownsItem ? _owner : Guid.NewGuid()), default);

        if (ownsItem) result!.Id.Should().Be(item.Id);
        else result.Should().BeNull();
    }

    private StudyItem CreateItem() => new("Original title", "Original description", StudyItemType.Programming,
        DifficultyLevel.Beginner, 30, 3, Guid.NewGuid(), _owner);

    private void SetupItem(StudyItem item) =>
        _repo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

    private Task Mutate(string operation, Guid id, Guid userId) => operation switch
    {
        "edit" => new UpdateStudyItemHandler(_repo.Object).Handle(new UpdateStudyItemCommand(id,
            "Updated title", "Updated description", StudyItemType.Algorithm, DifficultyLevel.Advanced,
            45, 5, Guid.NewGuid(), null, userId), default),
        "delete" => new DeleteStudyItemHandler(_repo.Object).Handle(new DeleteStudyItemCommand(id, userId), default),
        "toggle" => new ToggleCompleteHandler(_repo.Object).Handle(new ToggleCompleteCommand(id, userId), default),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
