using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.Queries.Sessions;
using StudyPlanner.Application.Queries.Users;
using StudyPlanner.Application.Validators;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.UnitTests;

public class ProgressHistoryTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(481, null)]
    [InlineData(20, -0.01)]
    [InlineData(20, 1.01)]
    [InlineData(20, double.NaN)]
    [InlineData(20, double.PositiveInfinity)]
    public async Task InvalidActualResults_AreRejectedByValidatorAndHandlerBeforeWrites(int minutes, double? score)
    {
        var store = new SecurityTestStore();
        var command = new CompleteSessionCommand(store.OwnSession.Id, store.Owner.Id, new() { new(store.OwnItem.Id, minutes, score, "Notes") });
        new CompleteSessionValidator().Validate(command).IsValid.Should().BeFalse();
        var handler = new CompleteSessionHandler(store.SessionRepository.Object, store.ItemRepository.Object, store.ProgressRepository.Object);
        (await Record.ExceptionAsync(() => handler.Handle(command, default))).Should().BeOfType<InvalidSessionCompletionException>();
        store.Writes.Should().Be(0); store.Progress.Should().BeEmpty(); store.OwnSession.Status.Should().Be(SessionStatus.Planned);
    }

    [Fact]
    public async Task OversizedNotes_AreRejectedBeforeWrites()
    {
        var store = new SecurityTestStore();
        var command = new CompleteSessionCommand(store.OwnSession.Id, store.Owner.Id, new() { new(store.OwnItem.Id, 20, null, new string('n', 1001)) });
        new CompleteSessionValidator().Validate(command).IsValid.Should().BeFalse();
        (await Record.ExceptionAsync(() => new CompleteSessionHandler(store.SessionRepository.Object, store.ItemRepository.Object, store.ProgressRepository.Object)
            .Handle(command, default))).Should().BeOfType<InvalidSessionCompletionException>();
        store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void OptionalScoreBoundaries_AreAccepted(double? score)
    {
        new CompleteSessionValidator().Validate(new CompleteSessionCommand(Guid.NewGuid(), Guid.NewGuid(), new()
        { new(Guid.NewGuid(), 480, score, null) })).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidationFailure_PreservesSubmittedActualScoreNotesAndSparseRowErrors()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var second = ReviewLifecycleTests.NewItem(factory.Store.Owner.Id, "Second item"); factory.Store.Items.Add(second.Id, second);
        factory.Store.OwnSession.AddItem(new(factory.Store.OwnSession.Id, second.Id, 20, 2));
        var token = await Token(client, "/Sessions/Record/" + factory.Store.OwnSession.Id);
        var writes = factory.Store.Writes;
        using var response = await client.PostAsync("/Sessions/CompleteSession", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SessionId"] = factory.Store.OwnSession.Id.ToString(), ["Results.Index"] = "1", ["Results[1].StudyItemId"] = second.Id.ToString(),
            ["Results[1].MinutesSpent"] = "17", ["Results[1].Score"] = "1.2", ["Results[1].Notes"] = "Preserved study notes", ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("value=\"17\"").And.Contain("value=\"1.2\"").And.Contain("Preserved study notes")
            .And.Contain("Score must be between 0 and 1.").And.Contain("data-valmsg-for=\"Results[1].Score\"");
        factory.Store.Writes.Should().Be(writes); factory.Store.Progress.Should().BeEmpty();
    }

    [Fact]
    public async Task ActualEntryAndHistory_KeepPlannedAndRecordedValuesDistinctWithNoReadWrites()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var before = await client.GetStringAsync("/Sessions/Record/" + factory.Store.OwnSession.Id);
        before.Should().Contain("Planned: 20 min").And.Contain("Actual minutes studied");
        Regex.Match(before, "<input[^>]*id=\"minutes-0\"[^>]*>").Value.Should().NotContain("value=\"20\"");
        var token = await Token(client, "/Sessions/Record/" + factory.Store.OwnSession.Id);
        using var result = await client.PostAsync("/Sessions/CompleteSession", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SessionId"] = factory.Store.OwnSession.Id.ToString(), ["Results[0].StudyItemId"] = factory.Store.OwnItem.Id.ToString(),
            ["Results[0].MinutesSpent"] = "7", ["Results[0].Score"] = "0.75", ["Results[0].Notes"] = "Private <study> notes", ["__RequestVerificationToken"] = token
        }));
        result.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Store.Progress.Should().ContainSingle().Which.MinutesSpent.Should().Be(7);
        var writes = factory.Store.Writes;
        var details = await client.GetStringAsync("/Sessions/Details/" + factory.Store.OwnSession.Id);
        details.Should().Contain("20 min").And.Contain("7 min").And.Contain("0.75").And.Contain("Private &lt;study&gt; notes");
        var history = await client.GetStringAsync("/Sessions/History"); history.Should().Contain("7 min").And.Contain("Completed");
        (await client.GetStringAsync("/")).Should().Contain("/Sessions/History");
        using var scope = factory.Services.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        (await mediator.Send(new GetDashboardQuery(factory.Store.Owner.Id))).TotalMinutesStudied.Should().Be(7);
        await client.GetAsync("/Focus"); factory.Store.Writes.Should().Be(writes);
        using var replay = await client.PostAsync("/Sessions/CompleteSession", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SessionId"] = factory.Store.OwnSession.Id.ToString(), ["Results[0].StudyItemId"] = factory.Store.OwnItem.Id.ToString(),
            ["Results[0].MinutesSpent"] = "7", ["__RequestVerificationToken"] = token
        }));
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest); factory.Store.Progress.Should().ContainSingle();
    }

    [Fact]
    public async Task HistoryAndDetails_RejectForeignSessionsAndNotesAtApplicationAndWebBoundaries()
    {
        using var factory = new SecurityWebApplicationFactory();
        factory.Store.Progress.Add(new(factory.Store.ForeignItem.UserId, factory.Store.ForeignItem.Id, 99, factory.Store.ForeignSession.Id, notes: "Foreign private notes"));
        using var scope = factory.Services.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        (await mediator.Send(new GetSessionDetailsQuery(factory.Store.ForeignSession.Id, factory.Store.Owner.Id))).Should().BeNull();
        factory.Store.ProgressRepository.Verify(r => r.GetBySessionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        using var client = factory.NewClient(); await SignIn(client);
        (await client.GetAsync("/Sessions/Details/" + factory.Store.ForeignSession.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/Sessions/Record/" + factory.Store.ForeignSession.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetStringAsync("/Sessions/History")).Should().NotContain("Foreign private notes").And.NotContain(factory.Store.ForeignSession.Id.ToString());
        factory.Store.ProgressRepository.Setup(r => r.GetBySessionAsync(factory.Store.Owner.Id, factory.Store.OwnSession.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(factory.Store.Progress);
        (await mediator.Send(new GetSessionDetailsQuery(factory.Store.OwnSession.Id, factory.Store.Owner.Id)))!.Activities.Should().BeEmpty();
    }

    [Fact]
    public async Task History_ClampsPaginationAndOrdersWithoutLoadingAllSessions()
    {
        using var factory = new SecurityWebApplicationFactory();
        for (var i = 0; i < 25; i++) { var session = new StudySession(factory.Store.Owner.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 45); factory.Store.Sessions.Add(session.Id, session); }
        using var scope = factory.Services.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var page = await mediator.Send(new GetSessionHistoryQuery(factory.Store.Owner.Id, 2, 10));
        page.Items.Should().HaveCount(10).And.BeInDescendingOrder(i => i.PlannedDate); page.Total.Should().Be(26);
        var clamped = await mediator.Send(new GetSessionHistoryQuery(factory.Store.Owner.Id, -1, 999)); clamped.Page.Should().Be(1); clamped.PageSize.Should().Be(50);
        factory.Store.SessionRepository.Verify(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        page.Items.Should().OnlyContain(i => i.RecordedMinutes == null && i.CompletedItems == 0);
    }

    internal static async Task SignIn(HttpClient client)
    {
        var token = await Token(client, "/Auth/Login");
        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword, ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
    internal static async Task<string> Token(HttpClient client, string path)
    {
        var match = Regex.Match(await client.GetStringAsync(path), "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        match.Success.Should().BeTrue(); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
