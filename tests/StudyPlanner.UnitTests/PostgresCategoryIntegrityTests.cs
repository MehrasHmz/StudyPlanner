using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Repositories;

namespace StudyPlanner.UnitTests;

public class PostgresCategoryIntegrityTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresCategoryIntegrityTests(PostgresTestDatabase database) => _database = database;

    [PostgresFact]
    public async Task ArchivedCategory_DoesNotHideActiveOwnedItemsOrExposeForeignOrDeletedItems()
    {
        await using var setup = _database.CreateContext();
        var owner = new User("Owner", Guid.NewGuid() + "@example.test", "test-only");
        var foreign = new User("Foreign", Guid.NewGuid() + "@example.test", "test-only");
        var category = new Category("Archive " + Guid.NewGuid());
        StudyItem Item(Guid user) => new("Archive item", "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, category.Id, user);
        var active = Item(owner.Id); active.SetNextReviewDate(DateTime.UtcNow.AddDays(-1));
        var deleted = Item(owner.Id); deleted.SoftDelete(); var other = Item(foreign.Id);
        setup.Users.AddRange(owner, foreign); setup.Categories.Add(category); setup.StudyItems.AddRange(active, deleted, other);
        await setup.SaveChangesAsync(); category.SoftDelete(); await setup.SaveChangesAsync();
        await using var context = _database.CreateContext();
        // Reproduce the required-navigation/global-filter failure before testing the corrected query.
        (await context.StudyItems.Include(i => i.Category).Where(i => i.UserId == owner.Id).ToListAsync()).Should().BeEmpty();
        var repo = new StudyItemRepository(context);
        (await repo.GetByUserIdAsync(owner.Id)).Should().ContainSingle().Which.Id.Should().Be(active.Id);
        (await repo.GetPagedAsync(owner.Id, 1, 10, null, null, false)).Total.Should().Be(1);
        (await repo.GetDueForReviewAsync(owner.Id, DateTime.UtcNow)).Should().ContainSingle().Which.Id.Should().Be(active.Id);
        (await repo.GetWithDetailsAsync(active.Id))!.Category.IsDeleted.Should().BeTrue();
        (await repo.GetWithDetailsAsync(deleted.Id)).Should().BeNull();
        (await new CategoryRepository(context).GetPagedAsync(1, 100)).Should().NotContain(c => c.Id == category.Id);
        (await new CategoryRepository(context).GetByNameAsync(category.Name))!.Id.Should().Be(category.Id);
    }

    [PostgresFact]
    public async Task DeletionGuard_PreservesCategoryReferencedEvenBySoftDeletedStudyItem()
    {
        await using var setup = _database.CreateContext();
        var owner = new User("Owner", Guid.NewGuid() + "@example.test", "test-only");
        var category = new Category("Referenced " + Guid.NewGuid());
        var item = new StudyItem("Historical", "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, category.Id, owner.Id); item.SoftDelete();
        setup.Users.Add(owner); setup.Categories.Add(category); setup.StudyItems.Add(item); await setup.SaveChangesAsync();
        await using var context = _database.CreateContext();
        (await Record.ExceptionAsync(() => new DeleteCategoryHandler(new CategoryRepository(context)).Handle(new(category.Id), default)))
            .Should().BeOfType<InvalidOperationException>();
        (await context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id)).IsDeleted.Should().BeFalse();
        (await context.SaveChangesAsync()).Should().Be(0);
    }
}
