using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Users;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Services;
using StudyPlanner.Infrastructure.Repositories;

namespace StudyPlanner.UnitTests;

public class PostgresReviewLifecycleTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresReviewLifecycleTests(PostgresTestDatabase database) => _database = database;

    [PostgresFact]
    public async Task SqlDueQueryAndDashboard_MatchPlannerAtExactBoundaryAndExcludeForeignDeletedFutureAndUndated()
    {
        var data = await Seed(); var now = data.Clock.Now.UtcDateTime;
        await using var context = _database.CreateContext();
        var items = new StudyItemRepository(context); var sessions = new StudySessionRepository(context); var progress = new ProgressRecordRepository(context);
        (await items.GetDueForReviewAsync(data.Owner, now.AddTicks(-10))).Should().BeEmpty();
        (await items.GetDueForReviewAsync(data.Owner, now)).Select(i => i.Id).Should().BeEquivalentTo(new[] { data.ReviewId });
        var dashboard = await new GetDashboardHandler(items, sessions, progress, data.Clock).Handle(new(data.Owner), default);
        dashboard.DueForReview.Should().Be(1); dashboard.CompletedItems.Should().Be(3); dashboard.TotalItems.Should().Be(4);
        var plan = await new PlanSessionHandler(sessions, items, new StudySessionPlanner(), data.Clock).Handle(new(data.Owner, 480), default);
        plan.Items.Select(i => i.StudyItemId).Should().BeEquivalentTo(new[] { data.ReviewId, data.PendingId });
        var planned = await context.StudySessions.SingleAsync(s => s.Id == plan.Id);
        planned.UserId.Should().Be(data.Owner); planned.PlannedDate.Should().Be(now);
    }

    [PostgresFact]
    public async Task MultiplePersistedReviewCycles_PreserveOriginalProgressAdvanceDatesAndAllowValidSubset()
    {
        var data = await Seed();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            Guid sessionId;
            await using (var context = _database.CreateContext())
            {
                var plan = await new PlanSessionHandler(new StudySessionRepository(context), new StudyItemRepository(context), new StudySessionPlanner(), data.Clock)
                    .Handle(new(data.Owner, 40), default);
                plan.Items.Select(i => i.StudyItemId).Should().Contain(data.ReviewId).And.Contain(data.PendingId);
                sessionId = plan.Id;
            }
            var command = new CompleteSessionCommand(sessionId, data.Owner, new() { new(data.ReviewId, 23 + cycle, 0.9, "Cycle " + cycle) });
            await using (var context = _database.CreateContext()) await Handler(context, data.Clock).Handle(command, default);
            await using (var repeat = _database.CreateContext())
                (await Record.ExceptionAsync(() => Handler(repeat, data.Clock).Handle(command, default))).Should().BeOfType<InvalidSessionCompletionException>();
            await using (var verify = _database.CreateContext())
            {
                var item = await verify.StudyItems.SingleAsync(i => i.Id == data.ReviewId);
                item.IsCompleted.Should().BeTrue(); item.NextReviewDate.Should().Be(data.Clock.Now.UtcDateTime.AddDays(7));
                (await verify.StudyItems.SingleAsync(i => i.Id == data.PendingId)).IsCompleted.Should().BeFalse();
                var records = await verify.ProgressRecords.Where(p => p.StudyItemId == data.ReviewId).ToListAsync();
                records.Should().HaveCount(cycle + 2);
                records.Single(p => p.Id == data.HistoryId).Should().Match<ProgressRecord>(p => p.MinutesSpent == 11 && p.Notes == "Original progress" && p.SessionId == null);
                records.Count(p => p.SessionId == sessionId).Should().Be(1);
                records.Single(p => p.SessionId == sessionId).CompletedAt.Should().Be(data.Clock.Now.UtcDateTime);
                (await new StudyItemRepository(verify).GetDueForReviewAsync(data.Owner, data.Clock.Now.UtcDateTime)).Select(i => i.Id).Should().NotContain(data.ReviewId);
            }
            data.Clock.Now = data.Clock.Now.AddDays(7);
        }
    }

    private static CompleteSessionHandler Handler(StudyPlanner.Infrastructure.Data.AppDbContext context, TimeProvider clock) =>
        new(new StudySessionRepository(context), new StudyItemRepository(context), new ProgressRecordRepository(context), clock);

    private async Task<SeedData> Seed()
    {
        var clock = new ReviewTestClock();
        await using var context = _database.CreateContext();
        var owner = new User("Review owner", Guid.NewGuid() + "@example.test", "test-only-hash");
        var foreign = new User("Foreign owner", Guid.NewGuid() + "@example.test", "test-only-hash");
        var category = new Category("Review " + Guid.NewGuid());
        StudyItem Item(Guid user, string title) => new(title, "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, category.Id, user);
        var due = Item(owner.Id, "Due completed review"); due.MarkCompleted(); due.SetNextReviewDate(clock.Now.UtcDateTime);
        var future = Item(owner.Id, "Future completed review"); future.MarkCompleted(); future.SetNextReviewDate(clock.Now.UtcDateTime.AddDays(100));
        var undated = Item(owner.Id, "Completed without date"); undated.MarkCompleted();
        var pending = Item(owner.Id, "Unfinished");
        var deleted = Item(owner.Id, "Deleted review"); deleted.MarkCompleted(); deleted.SetNextReviewDate(clock.Now.UtcDateTime); deleted.SoftDelete();
        var other = Item(foreign.Id, "Foreign review"); other.MarkCompleted(); other.SetNextReviewDate(clock.Now.UtcDateTime);
        var history = new ProgressRecord(owner.Id, due.Id, 11, notes: "Original progress");
        context.Users.AddRange(owner, foreign); context.Categories.Add(category);
        context.StudyItems.AddRange(due, future, undated, pending, deleted, other); context.ProgressRecords.Add(history);
        await context.SaveChangesAsync();
        return new(owner.Id, due.Id, pending.Id, history.Id, clock);
    }

    private record SeedData(Guid Owner, Guid ReviewId, Guid PendingId, Guid HistoryId, ReviewTestClock Clock);
}
