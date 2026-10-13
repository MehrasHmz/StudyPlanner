using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Users;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Services;

namespace StudyPlanner.UnitTests;

public sealed class ReviewTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public class ReviewLifecycleTests
{
    [Theory]
    [InlineData(false, null, false, true, false)]
    [InlineData(false, 1, false, true, false)]
    [InlineData(false, 0, false, true, true)]
    [InlineData(true, null, false, false, false)]
    [InlineData(true, 1, false, false, false)]
    [InlineData(true, 0, false, true, true)]
    [InlineData(true, -1, false, true, true)]
    [InlineData(true, -1, true, false, false)]
    [InlineData(false, null, true, false, false)]
    public void Eligibility_RespectsCompletionDateBoundaryAndDeletion(bool completed, int? ticks, bool deleted, bool eligible, bool due)
    {
        var now = new ReviewTestClock().Now.UtcDateTime;
        var item = NewItem(Guid.NewGuid());
        if (completed) item.MarkCompleted();
        item.SetNextReviewDate(ticks.HasValue ? now.AddTicks(ticks.Value) : null);
        if (deleted) item.SoftDelete();
        item.IsEligibleForStudy(now).Should().Be(eligible);
        item.IsDueForReview(now).Should().Be(due);
        new StudySessionPlanner().SelectItems(new[] { item }, 20, now).Contains(item).Should().Be(eligible);
        item.IsCompleted.Should().Be(completed);
    }

    [Fact]
    public void LocalAndUnspecifiedDates_UseConsistentUtcInterpretation()
    {
        var now = new ReviewTestClock().Now.UtcDateTime;
        var item = NewItem(Guid.NewGuid()); item.MarkCompleted();
        item.SetNextReviewDate(now.ToLocalTime());
        item.NextReviewDate.Should().Be(now);
        item.IsDueForReview(now).Should().BeTrue();
        item.SetNextReviewDate(DateTime.SpecifyKind(now, DateTimeKind.Unspecified));
        item.IsDueForReview(now).Should().BeTrue();
    }

    [Fact]
    public async Task DashboardPlannerAndLabels_AgreeWithoutReclassifyingCompletedItems()
    {
        var clock = new ReviewTestClock();
        using var factory = new SecurityWebApplicationFactory { Clock = clock };
        var store = factory.Store;
        store.OwnItem.MarkCompleted(); store.OwnItem.SetNextReviewDate(clock.Now.UtcDateTime);
        var future = NewItem(store.Owner.Id, "Future review"); future.MarkCompleted(); future.SetNextReviewDate(clock.Now.UtcDateTime.AddTicks(1));
        var unscheduled = NewItem(store.Owner.Id, "No review"); unscheduled.MarkCompleted();
        var pending = NewItem(store.Owner.Id, "Unfinished");
        var deleted = NewItem(store.Owner.Id, "Deleted"); deleted.SetNextReviewDate(clock.Now.UtcDateTime); deleted.SoftDelete();
        store.ForeignItem.SetNextReviewDate(clock.Now.UtcDateTime);
        foreach (var item in new[] { future, unscheduled, pending, deleted }) store.Items.Add(item.Id, item);
        store.ItemRepository.Setup(r => r.GetByUserIdAsync(store.Owner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => store.Items.Values.ToList());
        using var scope = factory.Services.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var dashboard = await mediator.Send(new GetDashboardQuery(store.Owner.Id));
        var plan = await mediator.Send(new PlanSessionCommand(store.Owner.Id, 480));
        dashboard.TotalItems.Should().Be(4); dashboard.CompletedItems.Should().Be(3); dashboard.DueForReview.Should().Be(1);
        dashboard.AsOfUtc.Should().Be(clock.Now.UtcDateTime);
        plan.Items.Select(i => i.StudyItemId).Should().BeEquivalentTo(new[] { store.OwnItem.Id, pending.Id });
        plan.Items.Count(i => store.Items[i.StudyItemId].IsDueForReview(dashboard.AsOfUtc)).Should().Be(dashboard.DueForReview);
        using var client = factory.NewClient(); await SignIn(client);
        var html = await client.GetStringAsync("/");
        html.Should().Contain("1 item due for review").And.Contain("Review due").And.Contain("Completed").And.Contain("Review Oct 13, 2026 UTC");
        var itemsHtml = await client.GetStringAsync("/StudyItems");
        itemsHtml.Should().Contain("Review due").And.Contain("Scheduled Oct 13, 2026 UTC").And.Contain("Not scheduled");
    }

    [Fact]
    public async Task RepeatedReviews_KeepHistoryAdvanceSevenDaysAndRejectSameSessionReplay()
    {
        var clock = new ReviewTestClock(); using var factory = new SecurityWebApplicationFactory { Clock = clock };
        var store = factory.Store;
        store.OwnItem.MarkCompleted(); store.OwnItem.SetNextReviewDate(clock.Now.UtcDateTime);
        var original = new ProgressRecord(store.Owner.Id, store.OwnItem.Id, 12, notes: "Historical learning"); store.Progress.Add(original);
        using var scope = factory.Services.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var plan = await mediator.Send(new PlanSessionCommand(store.Owner.Id, 20));
            plan.Items.Should().ContainSingle().Which.StudyItemId.Should().Be(store.OwnItem.Id);
            var command = new CompleteSessionCommand(plan.Id, store.Owner.Id, new() { new(store.OwnItem.Id, 20, 0.9, "Review " + cycle) });
            await mediator.Send(command);
            store.OwnItem.IsCompleted.Should().BeTrue(); store.OwnItem.NextReviewDate.Should().Be(clock.Now.UtcDateTime.AddDays(7));
            store.OwnItem.IsEligibleForStudy(clock.Now.UtcDateTime).Should().BeFalse();
            var replay = await Record.ExceptionAsync(() => mediator.Send(command)); replay.Should().BeOfType<InvalidSessionCompletionException>();
            store.Progress.Should().HaveCount(cycle + 2).And.Contain(original);
            store.Progress.Last().CompletedAt.Should().Be(clock.Now.UtcDateTime);
            original.MinutesSpent.Should().Be(12); original.Notes.Should().Be("Historical learning");
            clock.Now = clock.Now.AddDays(7);
        }
    }

    [Theory]
    [InlineData("/Sessions")]
    [InlineData("/Sessions/Plan")]
    [InlineData("/Sessions/Plan?AvailableMinutes=120")]
    public async Task PlanningGet_RendersProtectedFormWithoutCreatingSession(string path)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        using var anonymous = await client.GetAsync(path); anonymous.StatusCode.Should().Be(HttpStatusCode.Redirect);
        await SignIn(client); var writes = factory.Store.Writes; var count = factory.Store.Sessions.Count;
        var html = await client.GetStringAsync(path);
        html.Should().Contain("action=\"/Sessions/Plan\"").And.Contain("__RequestVerificationToken").And.Contain("max=\"480\"");
        factory.Store.Writes.Should().Be(writes); factory.Store.Sessions.Should().HaveCount(count);
        (await client.GetStringAsync("/")).Should().Contain("href=\"/Sessions\"");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("481")]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidPlanningPost_PreservesErrorAndCreatesNothing(string? minutes)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var token = await Token(client, "/Sessions/Plan"); var writes = factory.Store.Writes; var count = factory.Store.Sessions.Count;
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        if (minutes != null) fields["AvailableMinutes"] = minutes;
        using var response = await client.PostAsync("/Sessions/Plan", new FormUrlEncodedContent(fields));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("field-validation-error");
        factory.Store.Writes.Should().Be(writes); factory.Store.Sessions.Should().HaveCount(count);
    }

    [Fact]
    public async Task ValidPlanningPost_UsesAuthenticatedOwnerAndRetainsCompletionAndFocusWorkflow()
    {
        var clock = new ReviewTestClock(); using var factory = new SecurityWebApplicationFactory { Clock = clock };
        factory.Store.OwnItem.MarkCompleted(); factory.Store.OwnItem.SetNextReviewDate(clock.Now.UtcDateTime);
        using var client = factory.NewClient(); await SignIn(client);
        var token = await Token(client, "/Sessions/Plan");
        using var response = await client.PostAsync("/Sessions/Plan", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["AvailableMinutes"] = "20", ["UserId"] = factory.Store.ForeignItem.UserId.ToString(), ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("/Sessions/CompleteSession").And.Contain("/Focus?itemId=");
        factory.Store.Sessions.Values.Last().UserId.Should().Be(factory.Store.Owner.Id);
        factory.Store.Progress.Should().BeEmpty();
        (await client.GetAsync("/Focus")).StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Store.Progress.Should().BeEmpty();
    }

    internal static StudyItem NewItem(Guid owner, string title = "Review item") => new(title, "", StudyItemType.Programming,
        DifficultyLevel.Beginner, 20, 3, Guid.NewGuid(), owner);
    private static async Task SignIn(HttpClient client)
    {
        var token = await Token(client, "/Auth/Login");
        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword, ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
    private static async Task<string> Token(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        match.Success.Should().BeTrue(); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
