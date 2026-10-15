using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StudyPlanner.Application.Authorization;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Application.Queries.Categories;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Repositories;

namespace StudyPlanner.UnitTests;

public class PostgresPersonalCategoryTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresPersonalCategoryTests(PostgresTestDatabase database) => _database = database;

    [PostgresFact]
    public async Task ActualQueriesAndConstraints_IsolateOwnersAndScopeActiveNames()
    {
        await using var context = _database.CreateContext();
        var owner = new User("Owner", Guid.NewGuid() + "@example.test", "test-only");
        var other = new User("Other", Guid.NewGuid() + "@example.test", "test-only");
        var name = "Personal " + Guid.NewGuid();
        var personal = new Category(name, "", owner.Id); var foreign = new Category(name, "", other.Id); var shared = new Category(name);
        context.Users.AddRange(owner, other); context.Categories.AddRange(personal, foreign, shared); await context.SaveChangesAsync();
        var repo = new CategoryRepository(context);
        var visible = await repo.GetPagedAsync(1, 100, default, owner.Id);
        visible.Should().Contain(c => c.Id == personal.Id).And.Contain(c => c.Id == shared.Id).And.NotContain(c => c.Id == foreign.Id);
        (await repo.GetCountAsync(default, owner.Id)).Should().Be(visible.Count);
        (await repo.GetPagedAsync(1, 100)).Should().NotContain(c => c.UserId.HasValue);
        var permission = new CategoryPermission(new TestCurrentUser(owner.Id), new UserRepository(context));
        var query = new GetCategoryByIdHandler(repo, new TestCurrentUser(owner.Id), permission);
        (await query.Handle(new(foreign.Id), default)).Should().BeNull();
        (await query.Handle(new(personal.Id), default))!.CanManage.Should().BeTrue();
        context.Categories.Add(new Category(name, "", owner.Id));
        (await Record.ExceptionAsync(() => context.SaveChangesAsync())).Should().BeOfType<DbUpdateException>();
        context.ChangeTracker.Clear();
        var stored = await context.Categories.SingleAsync(c => c.Id == personal.Id); stored.SoftDelete(); await context.SaveChangesAsync();
        context.Categories.Add(new Category(name, "", owner.Id)); await context.SaveChangesAsync();
        (await repo.GetActiveByNameAsync(name, owner.Id))!.Id.Should().NotBe(personal.Id);
    }

    [PostgresFact]
    public async Task ReferencedPersonalCategory_CanBeArchivedWithoutLosingItemsAndItsNameCanBeReused()
    {
        await using var context = _database.CreateContext();
        var owner = new User("Owner", Guid.NewGuid() + "@example.test", "test-only");
        var category = new Category("Archived " + Guid.NewGuid(), "", owner.Id);
        var item = new StudyItem("Preserved", "", StudyItemType.Other, DifficultyLevel.Beginner, 20, 3, category.Id, owner.Id);
        context.Users.Add(owner); context.Categories.Add(category); context.StudyItems.Add(item); await context.SaveChangesAsync();
        var repo = new CategoryRepository(context); var items = new StudyItemRepository(context);
        var permission = new CategoryPermission(new TestCurrentUser(owner.Id), new UserRepository(context));
        await new DeleteCategoryHandler(repo, permission).Handle(new(category.Id), default);
        (await items.GetByUserIdAsync(owner.Id)).Should().ContainSingle().Which.Id.Should().Be(item.Id);
        (await repo.GetPagedAsync(1, 100, default, owner.Id)).Should().NotContain(c => c.Id == category.Id);
        var id = await new CreateCategoryHandler(repo, permission).Handle(new(category.Name, ""), default);
        id.Should().NotBe(category.Id);
        (await context.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == category.Id)).IsDeleted.Should().BeTrue();
        await new UpdateStudyItemHandler(items, repo).Handle(new(item.Id, "Still editable", "", StudyItemType.Other,
            DifficultyLevel.Beginner, 20, 3, category.Id, null, owner.Id), default);
        item.Title.Should().Be("Still editable");
        var command = new CreateStudyItemCommand("New", "", StudyItemType.Other, DifficultyLevel.Beginner, 20, 3, category.Id, owner.Id, null);
        (await Record.ExceptionAsync(() => new CreateStudyItemHandler(items, repo).Handle(command, default))).Should().BeOfType<ValidationException>();
    }

    [PostgresFact]
    public async Task DirectItemHandlers_RejectForeignCategoryBeforeAnyWrites()
    {
        await using var context = _database.CreateContext();
        var owner = new User("Owner", Guid.NewGuid() + "@example.test", "test-only");
        var other = new User("Other", Guid.NewGuid() + "@example.test", "test-only");
        var category = new Category("Foreign " + Guid.NewGuid(), "", other.Id);
        context.Users.AddRange(owner, other); context.Categories.Add(category); await context.SaveChangesAsync();
        var command = new CreateStudyItemCommand("Forbidden", "", StudyItemType.Other, DifficultyLevel.Beginner, 20, 3, category.Id, owner.Id, null);
        (await Record.ExceptionAsync(() => new CreateStudyItemHandler(new StudyItemRepository(context), new CategoryRepository(context)).Handle(command, default)))
            .Should().BeOfType<ValidationException>();
        (await context.SaveChangesAsync()).Should().Be(0);
        (await context.StudyItems.AnyAsync(i => i.UserId == owner.Id)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task Rollback_RefusesToTurnPrivateCategoriesIntoSharedData()
    {
        await using var context = _database.CreateContext();
        var owner = new User("Owner", Guid.NewGuid() + "@example.test", "test-only");
        var category = new Category("Rollback " + Guid.NewGuid(), "", owner.Id);
        context.Users.Add(owner); context.Categories.Add(category); await context.SaveChangesAsync();
        (await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync("20261014075951_AddUserRolesAndRoleAudit")))
            .Should().BeOfType<Npgsql.PostgresException>();
        context.ChangeTracker.Clear();
        (await context.Categories.SingleAsync(c => c.Id == category.Id)).UserId.Should().Be(owner.Id);
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
}
