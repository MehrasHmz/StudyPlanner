using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.Queries.Sessions;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Repositories;

namespace StudyPlanner.UnitTests;

public class PostgresHistoryTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresHistoryTests(PostgresTestDatabase database) => _database = database;

    [PostgresFact]
    public async Task ActualSubsetCompletion_HistoryAndDetailsPersistCorrectResultsWithoutForeignNotesOrReadWrites()
    {
        var data = await Seed(); var clock = new ReviewTestClock();
        var command = new CompleteSessionCommand(data.Session.Id, data.Owner.Id, new() { new(data.Items[0].Id, 7, 0.75, "  Actual notes  ") });
        await using (var context = _database.CreateContext())
            await new CompleteSessionHandler(new StudySessionRepository(context), new StudyItemRepository(context), new ProgressRecordRepository(context), clock).Handle(command, default);
        await using (var replay = _database.CreateContext())
            (await Record.ExceptionAsync(() => new CompleteSessionHandler(new StudySessionRepository(replay), new StudyItemRepository(replay), new ProgressRecordRepository(replay), clock)
                .Handle(command, default))).Should().BeOfType<InvalidSessionCompletionException>();
        await using (var setup = _database.CreateContext())
        {
            var item = await setup.StudyItems.SingleAsync(i => i.Id == data.Items[0].Id); item.SoftDelete();
            setup.ProgressRecords.Add(new(data.Foreign.Id, data.ForeignItem.Id, 99, data.Session.Id, notes: "Foreign private notes"));
            await setup.SaveChangesAsync();
        }
        await using var verify = _database.CreateContext();
        var sessions = new StudySessionRepository(verify); var progress = new ProgressRecordRepository(verify);
        var details = await new GetSessionDetailsHandler(sessions, progress).Handle(new(data.Session.Id, data.Owner.Id), default);
        details!.Session.TotalPlannedMinutes.Should().Be(40); details.RecordedMinutes.Should().Be(7); details.CompletedItems.Should().Be(1);
        details.Activities.Should().ContainSingle().Which.Should().Match<StudyPlanner.Application.DTOs.ProgressRecordDto>(p =>
            p.MinutesSpent == 7 && p.Score == 0.75 && p.Notes == "Actual notes" && p.CompletedAt == clock.Now.UtcDateTime && p.Title == "Owned item 0");
        (await sessions.GetHistoryPageAsync(data.Owner.Id, 1, 10)).Items.Should().ContainSingle().Which.RecordedMinutes.Should().Be(7);
        (await sessions.GetOwnedWithItemsAsync(data.Session.Id, data.Foreign.Id)).Should().BeNull();
        (await progress.GetBySessionAsync(data.Foreign.Id, data.Session.Id)).Should().ContainSingle().Which.Notes.Should().Be("Foreign private notes");
        (await progress.GetBySessionAsync(data.Owner.Id, data.Session.Id)).Should().ContainSingle();
        (await verify.StudyItems.AsNoTracking().SingleAsync(i => i.Id == data.Items[1].Id)).IsCompleted.Should().BeFalse();
        var reviewed = await verify.StudyItems.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == data.Items[0].Id);
        reviewed.IsCompleted.Should().BeTrue(); reviewed.NextReviewDate.Should().Be(clock.Now.UtcDateTime.AddDays(7));
        (await verify.ProgressRecords.AsNoTracking().SingleAsync(p => p.Id == data.HistoryId)).Notes.Should().Be("Prior learning");
        verify.ChangeTracker.Entries().Should().BeEmpty();
        (await new StudySessionRepository(verify).SaveChangesAsync()).Should().Be(0);
    }

    [PostgresFact]
    public async Task SqlPagination_OrdersAndAggregatesOnlyOwnedRecordsWithoutInventingActualTime()
    {
        var data = await Seed();
        await using (var setup = _database.CreateContext())
        {
            for (var i = 0; i < 22; i++) setup.StudySessions.Add(new StudySession(data.Owner.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 45));
            setup.StudySessions.Add(new StudySession(data.Foreign.Id, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), 480));
            setup.ProgressRecords.AddRange(new ProgressRecord(data.Owner.Id, data.Items[0].Id, 3, data.Session.Id),
                new ProgressRecord(data.Owner.Id, data.Items[0].Id, 4, data.Session.Id),
                new ProgressRecord(data.Foreign.Id, data.ForeignItem.Id, 99, data.Session.Id));
            await setup.SaveChangesAsync();
        }
        await using var context = _database.CreateContext(); var repo = new StudySessionRepository(context);
        var first = await repo.GetHistoryPageAsync(data.Owner.Id, 1, 10); var second = await repo.GetHistoryPageAsync(data.Owner.Id, 2, 10);
        var third = await repo.GetHistoryPageAsync(data.Owner.Id, 3, 10);
        first.Total.Should().Be(23); first.Items.Should().HaveCount(10).And.BeInDescendingOrder(i => i.PlannedDate);
        second.Items.Should().HaveCount(10); third.Items.Should().HaveCount(3);
        first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)).Should().BeEmpty();
        var target = first.Items.Single(i => i.Id == data.Session.Id); target.RecordedMinutes.Should().Be(7); target.CompletedItems.Should().Be(1);
        first.Items.Concat(second.Items).Concat(third.Items).Where(i => i.Id != data.Session.Id).Should().OnlyContain(i => i.RecordedMinutes == null && i.CompletedItems == 0);
        (await repo.GetHistoryPageAsync(data.Foreign.Id, 1, 10)).Items.Should().ContainSingle().Which.UserId.Should().Be(data.Foreign.Id);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [PostgresTheory]
    [InlineData("minutes")]
    [InlineData("score")]
    [InlineData("notes")]
    public async Task InvalidActualData_LeavesSessionItemsAndHistoryUnchanged(string invalid)
    {
        var data = await Seed();
        var command = new CompleteSessionCommand(data.Session.Id, data.Owner.Id, new()
        { new(data.Items[0].Id, invalid == "minutes" ? 481 : 7, invalid == "score" ? 2.0 : null, invalid == "notes" ? new string('x', 1001) : "Valid") });
        await using (var context = _database.CreateContext())
            (await Record.ExceptionAsync(() => new CompleteSessionHandler(new StudySessionRepository(context), new StudyItemRepository(context), new ProgressRecordRepository(context))
                .Handle(command, default))).Should().BeOfType<InvalidSessionCompletionException>();
        await using var verify = _database.CreateContext();
        (await verify.StudySessions.SingleAsync(s => s.Id == data.Session.Id)).Status.Should().Be(SessionStatus.Planned);
        (await verify.ProgressRecords.CountAsync(p => p.SessionId == data.Session.Id)).Should().Be(0);
        (await verify.ProgressRecords.SingleAsync(p => p.Id == data.HistoryId)).Notes.Should().Be("Prior learning");
    }

    private async Task<SeedData> Seed()
    {
        await using var context = _database.CreateContext();
        var owner = new User("History owner", Guid.NewGuid() + "@example.test", "test-only-hash");
        var foreign = new User("Foreign", Guid.NewGuid() + "@example.test", "test-only-hash"); var category = new Category("History " + Guid.NewGuid());
        var items = Enumerable.Range(0, 2).Select(i => new StudyItem("Owned item " + i, "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, category.Id, owner.Id)).ToArray();
        var foreignItem = new StudyItem("Foreign item", "", StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, category.Id, foreign.Id);
        var session = new StudySession(owner.Id, new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc), 45);
        for (var i = 0; i < items.Length; i++) session.AddItem(new(session.Id, items[i].Id, 20, i + 1));
        var history = new ProgressRecord(owner.Id, items[0].Id, 11, notes: "Prior learning");
        context.Users.AddRange(owner, foreign); context.Categories.Add(category); context.StudyItems.AddRange(items.Append(foreignItem));
        context.StudySessions.Add(session); context.ProgressRecords.Add(history); await context.SaveChangesAsync();
        return new(owner, foreign, foreignItem, session, items, history.Id);
    }
    private record SeedData(User Owner, User Foreign, StudyItem ForeignItem, StudySession Session, StudyItem[] Items, Guid HistoryId);
}
