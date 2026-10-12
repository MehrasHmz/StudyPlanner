using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StudyPlanner.Application;
using StudyPlanner.Application.Authentication;
using StudyPlanner.Application.Behaviors;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.Commands.Users;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Application.Queries.Categories;
using StudyPlanner.Application.Queries.StudyItems;
using StudyPlanner.Application.Queries.Users;
using StudyPlanner.Application.Validators;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Infrastructure;
using StudyPlanner.Infrastructure.Authentication;
using StudyPlanner.Infrastructure.Data;
using StudyPlanner.Infrastructure.Repositories;

namespace StudyPlanner.UnitTests;

public class DependencyRemovalRegressionTests
{
    [Fact]
    public void ProductionRegistrations_ResolveWithoutAutoMapperAndPreserveValidationAndHashing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=dependency_regression_model_only"
        }).Build());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        scoped.GetRequiredService<IMediator>().Should().NotBeNull();
        scoped.GetRequiredService<IRequestHandler<RegisterCommand, AuthDto>>().Should().BeOfType<RegisterHandler>();
        scoped.GetRequiredService<IRequestHandler<GetDashboardQuery, DashboardDto>>().Should().BeOfType<GetDashboardHandler>();
        scoped.GetRequiredService<IRequestHandler<CompleteSessionCommand>>().Should().BeOfType<CompleteSessionHandler>();
        scoped.GetRequiredService<IValidator<CompleteSessionCommand>>().Should().BeOfType<CompleteSessionValidator>();
        scoped.GetServices<IPipelineBehavior<RegisterCommand, AuthDto>>()
            .Should().Contain(behavior => behavior is ValidationBehavior<RegisterCommand, AuthDto>);
        scoped.GetRequiredService<IStudySessionPlanner>().Should().NotBeNull();
        scoped.GetRequiredService<AppDbContext>().Should().NotBeNull();
        scoped.GetRequiredService<IUserRepository>().Should().BeOfType<UserRepository>();
        scoped.GetRequiredService<ICategoryRepository>().Should().BeOfType<CategoryRepository>();
        scoped.GetRequiredService<IStudyItemRepository>().Should().BeOfType<StudyItemRepository>();
        scoped.GetRequiredService<IStudySessionRepository>().Should().BeOfType<StudySessionRepository>();
        scoped.GetRequiredService<IProgressRecordRepository>().Should().BeOfType<ProgressRecordRepository>();
        scoped.GetRequiredService<IPasswordHasher<User>>().Should().BeOfType<PasswordHasher<User>>();
        scoped.GetRequiredService<IPasswordHashService>().Should().BeOfType<PasswordHashService>();
        services.Should().NotContain(descriptor => descriptor.ServiceType.Assembly.GetName().Name!.StartsWith("AutoMapper"));
        typeof(StudyPlanner.Application.DependencyInjection).Assembly.GetReferencedAssemblies()
            .Should().NotContain(assembly => assembly.Name!.StartsWith("AutoMapper"));
    }

    [Fact]
    public async Task ManualQueryMappings_PreserveStudyItemCategoryAndDashboardDtos()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var store = factory.Store;
        var expectedItem = new StudyItemDto(store.OwnItem.Id, "Test item", "Description", StudyItemType.Programming,
            DifficultyLevel.Beginner, 20, 3, null, false, store.Category.Id, "", store.Owner.Id, store.OwnItem.CreatedAt);

        var details = await mediator.Send(new GetStudyItemByIdQuery(store.OwnItem.Id, store.Owner.Id));
        var list = await mediator.Send(new GetStudyItemsQuery(store.Owner.Id));
        var categories = await mediator.Send(new GetAllCategoriesQuery());
        var dashboard = await mediator.Send(new GetDashboardQuery(store.Owner.Id));

        details.Should().Be(expectedItem);
        list.Items.Should().ContainSingle().Which.Should().Be(expectedItem);
        list.Total.Should().Be(1);
        list.Page.Should().Be(1);
        categories.Items.Should().ContainSingle().Which.Should().Be(new CategoryDto(store.Category.Id,
            "Test category", "Test description"));
        categories.Total.Should().Be(1);
        dashboard.Should().BeEquivalentTo(new DashboardDto(1, 0, 0, 1, 0, new() { expectedItem }));
    }

    [Fact]
    public async Task SessionPlanning_PreservesManualSessionAndMemberMapping()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var session = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new PlanSessionCommand(factory.Store.Owner.Id, 45));

        session.Id.Should().NotBeEmpty();
        session.Status.Should().Be(SessionStatus.Planned);
        session.AvailableMinutes.Should().Be(45);
        session.TotalPlannedItems.Should().Be(1);
        session.TotalPlannedMinutes.Should().Be(20);
        var item = session.Items.Should().ContainSingle().Which;
        item.Id.Should().NotBeEmpty();
        item.StudyItemId.Should().Be(factory.Store.OwnItem.Id);
        item.StudyItemTitle.Should().Be("Test item");
        item.PlannedMinutes.Should().Be(20);
        item.Order.Should().Be(1);
    }

    [Fact]
    public async Task RealApplicationStartupAndAuthenticatedDashboard_RenderWithoutMapperServices()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        using var loginPage = await client.GetAsync("/Auth/Login");
        loginPage.StatusCode.Should().Be(HttpStatusCode.OK);
        var match = Regex.Match(await loginPage.Content.ReadAsStringAsync(),
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();
        using var login = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var dashboard = await client.GetAsync("/");

        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("Test item");
        factory.Store.Owner.PasswordHash.Should().StartWith("Identity:");
    }
}
