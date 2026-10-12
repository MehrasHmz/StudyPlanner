using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StudyPlanner.Application;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Web.Controllers;

namespace StudyPlanner.UnitTests;

public class OwnershipControllerTests : IDisposable
{
    private readonly Guid _authenticatedUser = Guid.NewGuid();
    private readonly Mock<IStudyItemRepository> _items = new();
    private readonly Mock<IStudySessionRepository> _sessions = new();
    private readonly Mock<IProgressRecordRepository> _progress = new();
    private readonly ServiceProvider _services;

    public OwnershipControllerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(_items.Object);
        services.AddSingleton(_sessions.Object);
        services.AddSingleton(_progress.Object);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Edit_IgnoresForgedCommandUserIdAndUsesAuthenticatedOwner()
    {
        var item = CreateItem(_authenticatedUser);
        SetupItem(item);
        var controller = Initialize(new StudyItemsController(_services.GetRequiredService<IMediator>()));

        var result = await controller.Edit(EditCommand(item, Guid.NewGuid()));

        result.Should().BeOfType<RedirectToActionResult>();
        item.Title.Should().Be("Changed");
        _items.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("toggle")]
    public async Task ForeignItem_ReturnsNotFoundAndDoesNotMutate(string operation)
    {
        var foreignOwner = Guid.NewGuid();
        var item = CreateItem(foreignOwner);
        SetupItem(item);
        var controller = Initialize(new StudyItemsController(_services.GetRequiredService<IMediator>()));

        var result = operation switch
        {
            "edit" => await controller.Edit(EditCommand(item, foreignOwner)),
            "delete" => await controller.Delete(item.Id),
            _ => await controller.ToggleComplete(item.Id)
        };

        result.Should().BeOfType<NotFoundResult>();
        item.Title.Should().Be("Original");
        item.IsDeleted.Should().BeFalse();
        item.IsCompleted.Should().BeFalse();
        _items.Verify(r => r.Update(It.IsAny<StudyItem>()), Times.Never);
        _items.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidForeignEdit_DoesNotRenderForeignDataInValidationErrorView()
    {
        var item = CreateItem(Guid.NewGuid());
        SetupItem(item);
        var controller = Initialize(new StudyItemsController(_services.GetRequiredService<IMediator>()));

        var result = await controller.Edit(EditCommand(item, item.UserId) with { Title = "" });

        result.Should().BeOfType<NotFoundResult>();
        _items.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("toggle")]
    public async Task MissingItem_ReturnsSameNotFoundResponse(string operation)
    {
        var item = CreateItem(_authenticatedUser);
        var controller = Initialize(new StudyItemsController(_services.GetRequiredService<IMediator>()));

        var result = operation switch
        {
            "edit" => await controller.Edit(EditCommand(item, _authenticatedUser)),
            "delete" => await controller.Delete(item.Id),
            _ => await controller.ToggleComplete(item.Id)
        };

        result.Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignOrMissingSession_ReturnsNotFound(bool exists)
    {
        var session = new StudySession(Guid.NewGuid(), DateTime.UtcNow, 60);
        if (exists)
            _sessions.Setup(r => r.GetWithItemsAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var controller = Initialize(new SessionsController(_services.GetRequiredService<IMediator>()));

        var result = await controller.CompleteSession(session.Id, new() { new(Guid.NewGuid(), 20, null, "") });

        result.Should().BeOfType<NotFoundResult>();
        AssertNoSessionWrites();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("zero")]
    [InlineData("empty")]
    [InlineData("binding-error")]
    [InlineData("unrelated")]
    [InlineData("completed")]
    public async Task InvalidCompletion_ReturnsBadRequestWithoutFilteringOrWrites(string reason)
    {
        var item = CreateItem(_authenticatedUser);
        SetupItem(item);
        var session = new StudySession(_authenticatedUser, DateTime.UtcNow, 60);
        session.AddItem(new StudySessionItem(session.Id, item.Id, 20, 1));
        if (reason == "completed") { session.StartSession(); session.CompleteSession(); }
        _sessions.Setup(r => r.GetWithItemsAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var controller = Initialize(new SessionsController(_services.GetRequiredService<IMediator>()));
        var results = new List<ItemResultDto> { new(item.Id, 20, null, "") };
        if (reason == "duplicate") results.Add(results[0]);
        if (reason == "zero") results.Add(new(Guid.NewGuid(), 0, null, ""));
        if (reason == "empty") results.Clear();
        if (reason == "binding-error") controller.ModelState.AddModelError("Results[0].MinutesSpent", "Invalid number");
        if (reason == "unrelated") results.Add(new(Guid.NewGuid(), 20, null, ""));

        var result = await controller.CompleteSession(session.Id, results);

        result.Should().BeOfType<BadRequestObjectResult>();
        controller.TempData.ContainsKey("Success").Should().BeFalse();
        session.Status.Should().Be(reason == "completed" ? SessionStatus.Completed : SessionStatus.Planned);
        item.IsCompleted.Should().BeFalse();
        AssertNoSessionWrites();
    }

    [Fact]
    public async Task ValidCompletion_UsesClaimOwnerAndPreservesSuccessRedirect()
    {
        var item = CreateItem(_authenticatedUser);
        SetupItem(item);
        var session = new StudySession(_authenticatedUser, DateTime.UtcNow, 60);
        session.AddItem(new StudySessionItem(session.Id, item.Id, 20, 1));
        _sessions.Setup(r => r.GetWithItemsAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _progress.Setup(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProgressRecord record, CancellationToken _) => record);
        var controller = Initialize(new SessionsController(_services.GetRequiredService<IMediator>()));

        var result = await controller.CompleteSession(session.Id, new() { new(item.Id, 20, null, "") });

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
        controller.TempData.ContainsKey("Success").Should().BeTrue();
        session.Status.Should().Be(SessionStatus.Completed);
        _progress.Verify(r => r.AddAsync(It.Is<ProgressRecord>(p => p.UserId == _authenticatedUser),
            It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnexpectedPersistenceFailure_IsNotReportedAsInvalidUserInputOrSuccess()
    {
        var item = CreateItem(_authenticatedUser);
        SetupItem(item);
        var session = new StudySession(_authenticatedUser, DateTime.UtcNow, 60);
        session.AddItem(new StudySessionItem(session.Id, item.Id, 20, 1));
        _sessions.Setup(r => r.GetWithItemsAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _sessions.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Persistence failure"));
        _progress.Setup(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProgressRecord record, CancellationToken _) => record);
        var controller = Initialize(new SessionsController(_services.GetRequiredService<IMediator>()));

        var act = () => controller.CompleteSession(session.Id, new() { new(item.Id, 20, null, "") });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Persistence failure");
        controller.TempData.ContainsKey("Success").Should().BeFalse();
    }

    private T Initialize<T>(T controller) where T : Controller
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, _authenticatedUser.ToString())
            }, "Test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private void SetupItem(StudyItem item)
    {
        _items.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _items.Setup(r => r.GetWithDetailsAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
    }

    private static StudyItem CreateItem(Guid owner) => new("Original", "", StudyItemType.Programming,
        DifficultyLevel.Beginner, 20, 3, Guid.NewGuid(), owner);

    private static UpdateStudyItemCommand EditCommand(StudyItem item, Guid userId) => new(item.Id, "Changed", "",
        StudyItemType.Programming, DifficultyLevel.Beginner, 20, 3, item.CategoryId, null, userId);

    private void AssertNoSessionWrites()
    {
        _progress.Verify(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _items.Verify(r => r.Update(It.IsAny<StudyItem>()), Times.Never);
    }

    public void Dispose() => _services.Dispose();
}
