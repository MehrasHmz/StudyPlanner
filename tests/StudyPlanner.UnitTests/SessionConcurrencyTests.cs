using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Infrastructure.Data;
using StudyPlanner.Web.Controllers;

namespace StudyPlanner.UnitTests;

public class SessionConcurrencyTests
{
    [Fact]
    public void SessionStatus_IsAnExistingColumnConcurrencyToken()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var property = context.Model.FindEntityType(typeof(StudySession))!.FindProperty(nameof(StudySession.Status))!;
        property.IsConcurrencyToken.Should().BeTrue();
        property.IsShadowProperty().Should().BeFalse();
        context.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.WhenNeeded);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("persistence")]
    [InlineData("cancellation")]
    public async Task Handler_PropagatesSaveFailuresWithoutRetryOrFalseSuccess(string kind)
    {
        var owner = Guid.NewGuid();
        var item = new StudyItem("Item", "", StudyItemType.Programming, DifficultyLevel.Beginner,
            20, 3, Guid.NewGuid(), owner);
        var session = new StudySession(owner, DateTime.UtcNow, 60);
        session.AddItem(new StudySessionItem(session.Id, item.Id, 20, 1));
        var sessions = new Mock<IStudySessionRepository>();
        var items = new Mock<IStudyItemRepository>();
        var progress = new Mock<IProgressRecordRepository>();
        sessions.Setup(r => r.GetWithItemsAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        items.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        progress.Setup(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProgressRecord record, CancellationToken _) => record);
        Exception failure = kind switch
        {
            "conflict" => new SessionCompletionConflictException(new DbUpdateConcurrencyException("Internal detail")),
            "cancellation" => new OperationCanceledException(),
            _ => new DbUpdateException("Internal persistence detail")
        };
        sessions.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var handler = new CompleteSessionHandler(sessions.Object, items.Object, progress.Object);

        var observed = await Record.ExceptionAsync(() => handler.Handle(
            new CompleteSessionCommand(session.Id, owner, new() { new(item.Id, 20, null, "") }), default));

        observed.Should().BeSameAs(failure);
        sessions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Controller_MapsExpectedConflictToSafe409WithoutSuccessMessage()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CompleteSessionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionCompletionConflictException(new DbUpdateConcurrencyException("Secret SQL detail")));
        var controller = new SessionsController(mediator.Object);
        using var cancellation = new CancellationTokenSource();
        var http = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
            }, "Test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());

        var result = await controller.CompleteSession(Guid.NewGuid(), new() { new(Guid.NewGuid(), 20, null, "") });

        result.Should().BeOfType<ConflictObjectResult>().Which.Value!.ToString().Should().NotContain("Secret SQL");
        controller.TempData.ContainsKey("Success").Should().BeFalse();
        mediator.Verify(m => m.Send(It.IsAny<CompleteSessionCommand>(), cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ActualHttpPipeline_ValidCsrfAndCookieReturnSafe409ForExpectedConflict()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        var loginToken = await Token(client, "/Auth/Login");
        using var login = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword,
            ["__RequestVerificationToken"] = loginToken
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var token = await Token(client, "/Sessions");
        factory.Store.SessionRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionCompletionConflictException(new DbUpdateConcurrencyException("Secret SQL detail")));

        using var response = await client.PostAsync("/Sessions/CompleteSession", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SessionId"] = factory.Store.OwnSession.Id.ToString(),
            ["Results[0].StudyItemId"] = factory.Store.OwnItem.Id.ToString(),
            ["Results[0].MinutesSpent"] = "20", ["Results[0].Notes"] = "Reviewed",
            ["__RequestVerificationToken"] = token
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Secret SQL");
        factory.Store.SessionRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<string> Token(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var match = Regex.Match(await response.Content.ReadAsStringAsync(),
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
