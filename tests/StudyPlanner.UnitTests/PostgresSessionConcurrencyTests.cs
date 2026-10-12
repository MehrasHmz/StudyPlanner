using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Data;
using StudyPlanner.Infrastructure.Repositories;

namespace StudyPlanner.UnitTests;

public class PostgresSessionConcurrencyTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresSessionConcurrencyTests(PostgresTestDatabase database) => _database = database;

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OverlappingCompletions_CommitExactlyOneWinningResultSet(bool disjointSubsets, bool singleStatementBatches)
    {
        var data = await Seed();
        var first = Command(data, "first", 21);
        var second = Command(data, "second", 37);
        if (disjointSubsets)
        {
            first = first with { Results = new() { first.Results[0] } };
            second = second with { Results = new() { second.Results[1] } };
        }
        var gate = new SaveGate();
        var sql = new SqlObserver();
        await using var firstContext = _database.CreateContext(singleStatementBatches, gate, sql);
        await using var secondContext = _database.CreateContext(singleStatementBatches, gate, sql);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstTask = Attempt(firstContext, first, timeout.Token);
        var secondTask = Attempt(secondContext, second, timeout.Token);
        try
        {
            await gate.BothReady.Task.WaitAsync(timeout.Token);
            gate.OriginalStates.Should().HaveCount(2).And.OnlyContain(status => status == SessionStatus.Planned);
        }
        finally { gate.Release.TrySetResult(); }
        var outcomes = await Task.WhenAll(firstTask, secondTask).WaitAsync(timeout.Token);

        outcomes.Count(error => error == null).Should().Be(1);
        outcomes.Where(error => error != null).Should().ContainSingle()
            .Which.Should().BeOfType<SessionCompletionConflictException>();
        var winner = outcomes[0] == null ? first : second;
        var loserContext = outcomes[0] == null ? secondContext : firstContext;
        loserContext.ChangeTracker.Entries().Should().BeEmpty();
        // Reusing a failed scope cannot accidentally persist its abandoned graph.
        (await new StudySessionRepository(loserContext).SaveChangesAsync(timeout.Token)).Should().Be(0);
        await AssertCompleted(data, winner);
        sql.Commands.Count(command => command.Contains("UPDATE \"StudySessions\"", StringComparison.Ordinal))
            .Should().Be(2);
        sql.Commands.Where(command => command.Contains("UPDATE \"StudySessions\"", StringComparison.Ordinal))
            .Should().OnlyContain(command => Regex.IsMatch(command,
                "UPDATE \"StudySessions\"[^;]*WHERE[^;]*\"Status\" ="));
    }

    [PostgresFact]
    public async Task SequentialRepeat_IsRejectedWithoutAdditionalProgress()
    {
        var data = await Seed();
        var command = Command(data);
        await using (var context = _database.CreateContext()) await Handler(context).Handle(command, default);
        await using var repeated = _database.CreateContext();

        var error = await Record.ExceptionAsync(() => Handler(repeated).Handle(command, default));

        error.Should().BeOfType<InvalidSessionCompletionException>();
        await AssertCompleted(data, command);
    }

    [PostgresFact]
    public async Task IndependentSessions_CanCompleteConcurrently()
    {
        var first = await Seed();
        var second = await Seed();
        var gate = new SaveGate();
        await using var firstContext = _database.CreateContext(gate);
        await using var secondContext = _database.CreateContext(gate);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstTask = Attempt(firstContext, Command(first), timeout.Token);
        var secondTask = Attempt(secondContext, Command(second), timeout.Token);
        try { await gate.BothReady.Task.WaitAsync(timeout.Token); }
        finally { gate.Release.TrySetResult(); }

        var outcomes = await Task.WhenAll(firstTask, secondTask).WaitAsync(timeout.Token);

        outcomes.Should().OnlyContain(error => error == null);
        await AssertCompleted(first, Command(first));
        await AssertCompleted(second, Command(second));
    }

    [PostgresFact]
    public async Task DifferentSessions_SharingStudyItems_CanBothComplete()
    {
        var first = await Seed();
        var secondSession = new StudySession(first.Owner, DateTime.UtcNow, 60);
        for (var index = 0; index < first.Items.Length; index++)
            secondSession.AddItem(new StudySessionItem(secondSession.Id, first.Items[index].Id, 20, index + 1));
        await using (var setup = _database.CreateContext())
        {
            setup.StudySessions.Add(secondSession);
            await setup.SaveChangesAsync();
        }
        var second = new SeedData(first.Owner, secondSession, first.Items);
        var gate = new SaveGate();
        await using var firstContext = _database.CreateContext(false, gate);
        await using var secondContext = _database.CreateContext(false, gate);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstTask = Attempt(firstContext, Command(first, "session-one"), timeout.Token);
        var secondTask = Attempt(secondContext, Command(second, "session-two"), timeout.Token);
        try { await gate.BothReady.Task.WaitAsync(timeout.Token); }
        finally { gate.Release.TrySetResult(); }

        var outcomes = await Task.WhenAll(firstTask, secondTask).WaitAsync(timeout.Token);

        outcomes.Should().OnlyContain(error => error == null);
        await AssertCompleted(first, Command(first, "session-one"));
        await AssertCompleted(second, Command(second, "session-two"));
    }

    [PostgresTheory]
    [InlineData("foreign-session")]
    [InlineData("unrelated-item")]
    [InlineData("foreign-member")]
    [InlineData("deleted-member")]
    [InlineData("duplicate")]
    [InlineData("invalid-minutes")]
    [InlineData("empty")]
    public async Task InvalidCompletion_PersistsNothing(string kind)
    {
        var data = await Seed();
        var command = Command(data);
        if (kind == "foreign-session") command = command with { UserId = Guid.NewGuid() };
        if (kind == "unrelated-item") command.Results[1] = command.Results[1] with { StudyItemId = Guid.NewGuid() };
        if (kind == "duplicate") command.Results[1] = command.Results[0];
        if (kind == "invalid-minutes") command.Results[1] = command.Results[1] with { MinutesSpent = 0 };
        if (kind == "empty") command.Results.Clear();
        if (kind is "foreign-member" or "deleted-member")
        {
            await using var setup = _database.CreateContext();
            if (kind == "deleted-member")
                await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StudyItems\" SET \"IsDeleted\" = TRUE WHERE \"Id\" = {data.Items[1].Id}");
            else
            {
                var foreign = new User("Foreign", Guid.NewGuid() + "@example.test", "test-only-hash");
                setup.Users.Add(foreign);
                await setup.SaveChangesAsync();
                await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StudyItems\" SET \"UserId\" = {foreign.Id} WHERE \"Id\" = {data.Items[1].Id}");
            }
        }
        await using var context = _database.CreateContext();

        var error = await Record.ExceptionAsync(() => Handler(context).Handle(command, default));

        if (kind == "foreign-session") error.Should().BeOfType<KeyNotFoundException>();
        else error.Should().BeOfType<InvalidSessionCompletionException>();
        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        await AssertUnchanged(data);
    }

    [PostgresFact]
    public async Task DatabaseFailureAtCommit_RollsBackSessionItemsAndAllProgress()
    {
        var data = await Seed();
        await using var setup = _database.CreateContext();
        // A deferred trigger fails only after all DML has executed, at transaction commit.
        // The only substituted literal is a locally generated, strongly typed GUID, not request input.
        var failureTriggerSql = $"""
            CREATE FUNCTION phase3_reject_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'phase3 injected commit failure' USING ERRCODE = '23514';
                RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER phase3_reject_completion
            AFTER UPDATE ON "StudySessions" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW WHEN (NEW."Id" = '{data.Session.Id}'::uuid)
            EXECUTE FUNCTION phase3_reject_completion();
            """;
        await setup.Database.ExecuteSqlRawAsync(failureTriggerSql);
        try
        {
            var observer = new SqlObserver();
            await using var context = _database.CreateContext(observer);

            var error = await Record.ExceptionAsync(() => Handler(context).Handle(Command(data), default));

            error.Should().NotBeNull().And.NotBeOfType<SessionCompletionConflictException>();
            error!.GetBaseException().Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("23514");
            observer.SuccessfulCommands.Count(command => command.Contains("INSERT INTO \"ProgressRecords\"", StringComparison.Ordinal))
                .Should().Be(2, "both inserts ran before the deferred database failure");
            observer.SuccessfulCommands.Should().Contain(command => command.Contains("UPDATE \"StudySessions\"", StringComparison.Ordinal));
            observer.SuccessfulCommands.Should().Contain(command => command.Contains("UPDATE \"StudyItems\"", StringComparison.Ordinal));
            context.ChangeTracker.Entries().Should().BeEmpty();
            await AssertUnchanged(data);
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync("DROP TRIGGER phase3_reject_completion ON \"StudySessions\"; DROP FUNCTION phase3_reject_completion();");
        }
        // A new request can complete after failure; neither connection nor transaction remains locked.
        await using var recovery = _database.CreateContext();
        await Handler(recovery).Handle(Command(data), default);
        await AssertCompleted(data, Command(data));
    }

    [PostgresFact]
    public async Task CancellationBeforeCommit_RollsBackWritesAndReleasesTransaction()
    {
        var data = await Seed();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var observer = new SqlObserver();
        await using (var context = _database.CreateContext(observer, new CancelAtCommit(cancellation)))
        {
            var error = await Record.ExceptionAsync(() => Handler(context).Handle(Command(data), cancellation.Token));

            error.Should().BeAssignableTo<OperationCanceledException>();
            observer.SuccessfulCommands.Should().Contain(command => command.Contains("INSERT INTO \"ProgressRecords\"", StringComparison.Ordinal));
            context.ChangeTracker.Entries().Should().BeEmpty();
        }
        await AssertUnchanged(data);
        await using var recovery = _database.CreateContext();
        await Handler(recovery).Handle(Command(data), default);
        await AssertCompleted(data, Command(data));
    }

    private async Task<SeedData> Seed()
    {
        await using var context = _database.CreateContext();
        var user = new User("Concurrency Test", Guid.NewGuid() + "@example.test", "test-only-hash");
        var category = new Category("Phase3 " + Guid.NewGuid(), "");
        var items = Enumerable.Range(0, 2).Select(index => new StudyItem("Item " + index, "",
            StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, category.Id, user.Id)).ToArray();
        var session = new StudySession(user.Id, DateTime.UtcNow, 60);
        for (var index = 0; index < items.Length; index++)
            session.AddItem(new StudySessionItem(session.Id, items[index].Id, 20, index + 1));
        context.Users.Add(user);
        context.Categories.Add(category);
        context.StudyItems.AddRange(items);
        context.StudySessions.Add(session);
        await context.SaveChangesAsync();
        return new SeedData(user.Id, session, items);
    }

    private static CompleteSessionHandler Handler(AppDbContext context) => new(new StudySessionRepository(context),
        new StudyItemRepository(context), new ProgressRecordRepository(context));
    private static CompleteSessionCommand Command(SeedData data, string notes = "Reviewed", int minutes = 20) =>
        new(data.Session.Id, data.Owner, data.Items.Select(item => new ItemResultDto(item.Id, minutes, 0.8, notes)).ToList());
    private static Task<Exception?> Attempt(AppDbContext context, CompleteSessionCommand command, CancellationToken ct) =>
        Record.ExceptionAsync(() => Handler(context).Handle(command, ct));

    private async Task AssertCompleted(SeedData data, CompleteSessionCommand winner)
    {
        await using var verify = _database.CreateContext();
        var session = await verify.StudySessions.Include(row => row.SessionItems).SingleAsync(row => row.Id == data.Session.Id);
        session.Status.Should().Be(SessionStatus.Completed);
        session.SessionItems.Should().HaveCount(2);
        var records = await verify.ProgressRecords.Where(record => record.SessionId == data.Session.Id).ToListAsync();
        records.Should().HaveCount(winner.Results.Count);
        foreach (var result in winner.Results)
        {
            records.Should().ContainSingle(record => record.StudyItemId == result.StudyItemId)
                .Which.Should().Match<ProgressRecord>(record => record.UserId == data.Owner &&
                    record.MinutesSpent == result.MinutesSpent && record.Notes == result.Notes && record.ResultScore == result.Score);
        }
        var ids = data.Items.Select(item => item.Id).ToArray();
        var storedItems = await verify.StudyItems.IgnoreQueryFilters().Where(item => ids.Contains(item.Id)).ToListAsync();
        foreach (var item in storedItems)
        {
            var completed = winner.Results.Any(result => result.StudyItemId == item.Id);
            item.IsCompleted.Should().Be(completed);
            if (completed) item.NextReviewDate.Should().NotBeNull();
            else item.NextReviewDate.Should().BeNull();
            item.Title.Should().Be(data.Items.Single(original => original.Id == item.Id).Title);
        }
    }

    private async Task AssertUnchanged(SeedData data)
    {
        await using var verify = _database.CreateContext();
        (await verify.StudySessions.SingleAsync(row => row.Id == data.Session.Id)).Status.Should().Be(SessionStatus.Planned);
        (await verify.ProgressRecords.CountAsync(record => record.SessionId == data.Session.Id)).Should().Be(0);
        var ids = data.Items.Select(item => item.Id).ToArray();
        var items = await verify.StudyItems.IgnoreQueryFilters().Where(item => ids.Contains(item.Id)).ToListAsync();
        items.Should().HaveCount(2).And.OnlyContain(item => !item.IsCompleted && item.NextReviewDate == null);
    }

    private sealed record SeedData(Guid Owner, StudySession Session, StudyItem[] Items);

    private sealed class SaveGate : SaveChangesInterceptor
    {
        private int _arrivals;
        public TaskCompletionSource BothReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<SessionStatus> OriginalStates { get; } = new();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var session = eventData.Context!.ChangeTracker.Entries<StudySession>().SingleOrDefault();
            if (session == null) return result;
            OriginalStates.Enqueue(session.Property(row => row.Status).OriginalValue);
            if (Interlocked.Increment(ref _arrivals) == 2) BothReady.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    private sealed class SqlObserver : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public ConcurrentQueue<string> SuccessfulCommands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            SuccessfulCommands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancelAtCommit : DbTransactionInterceptor
    {
        private readonly CancellationTokenSource _cancellation;
        public CancelAtCommit(CancellationTokenSource cancellation) => _cancellation = cancellation;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            throw new OperationCanceledException(_cancellation.Token);
        }
    }
}
