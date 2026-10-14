using System.Net;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StudyPlanner.Application.Authorization;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.UnitTests;

public class AdministratorAuthorizationTests
{
    [Fact]
    public async Task AnonymousCaller_CannotReachManagementOrMutateWithAnAnonymousFormToken()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        (await client.GetAsync("/Categories/Create")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var token = await ProgressHistoryTests.Token(client, "/Auth/Login"); var writes = factory.Store.Writes;
        using var response = await client.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Name"] = "Anonymous", ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect); response.Headers.Location!.OriginalString.Should().Contain("/Auth/Login");
        factory.Store.Writes.Should().Be(writes);
    }
    [Theory]
    [InlineData("/Categories/Create")]
    [InlineData("/Categories/Edit")]
    [InlineData("/Categories/Delete")]
    public async Task RegularUser_CannotMutateCatalogEvenWithValidCsrfAndForgedRoleFields(string path)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, "/StudyItems/Create"); var writes = factory.Store.Writes;
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = factory.Store.Category.Id.ToString(), ["Name"] = "Forged", ["Role"] = "Administrator", ["IsAdmin"] = "true",
            ["UserId"] = Guid.NewGuid().ToString(), ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden); factory.Store.Writes.Should().Be(writes);
        factory.Store.Category.IsDeleted.Should().BeFalse(); factory.Store.Category.Name.Should().Be("Test category");
    }

    [Theory]
    [InlineData("/Categories/Create")]
    [InlineData("/Categories/Edit/")]
    public async Task RegularUser_ManagementGetIsForbiddenAndSharedReadsRemainUsable(string path)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        using var response = await client.GetAsync(path.EndsWith('/') ? path + factory.Store.Category.Id : path);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Back to categories");
        (await client.GetStringAsync("/Categories")).Should().Contain("Test category").And.NotContain("/Categories/Create").And.NotContain("/Categories/Edit").And.NotContain("/Categories/Delete");
        (await client.GetStringAsync("/StudyItems/Create")).Should().Contain(factory.Store.Category.Id.ToString()).And.NotContain("New category");
        (await client.GetAsync("/api/categories/list")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registration_IgnoresOverpostedPrivilegesAndNewUsersAreRegular()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        var token = await ProgressHistoryTests.Token(client, "/Auth/Register");
        using var response = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["FullName"] = "Regular", ["Email"] = "overpost@example.test", ["Password"] = "Password-9!", ["Role"] = "1", ["IsAdmin"] = "true", ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Store.Users.Values.Single(u => u.Email == "overpost@example.test").Role.Should().Be(UserRole.Regular);
        factory.Store.Owner.Role.Should().Be(UserRole.Regular);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task DirectApplicationMutation_DeniesMissingTrustedIdentityBeforeAnyRepositoryWrites(string action)
    {
        using var factory = new SecurityWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        factory.Store.Owner.SetRole(UserRole.Administrator);
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var writes = factory.Store.Writes;
        var error = await Record.ExceptionAsync(async () =>
        {
            if (action == "create") await mediator.Send(new CreateCategoryCommand("Valid", ""));
            else if (action == "edit") await mediator.Send(new UpdateCategoryCommand(factory.Store.Category.Id, "Valid", ""));
            else await mediator.Send(new DeleteCategoryCommand(factory.Store.Category.Id));
        });
        error.Should().BeOfType<CategoryManagementDeniedException>(); factory.Store.Writes.Should().Be(writes);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task DirectHandlers_RejectAuthoritativeRegularCaller(string action)
    {
        using var factory = new SecurityWebApplicationFactory();
        var permission = new CategoryPermission(new TestCurrentUser(factory.Store.Owner.Id), factory.Store.UserRepository.Object);
        var writes = factory.Store.Writes;
        var error = await Record.ExceptionAsync(async () =>
        {
            if (action == "create") await new CreateCategoryHandler(factory.Store.CategoryRepository.Object, permission).Handle(new("Valid", ""), default);
            else if (action == "edit") await new UpdateCategoryHandler(factory.Store.CategoryRepository.Object, permission).Handle(new(factory.Store.Category.Id, "Valid", ""), default);
            else await new DeleteCategoryHandler(factory.Store.CategoryRepository.Object, permission).Handle(new(factory.Store.Category.Id), default);
        });
        error.Should().BeOfType<CategoryManagementDeniedException>(); factory.Store.Writes.Should().Be(writes);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task Administrator_PerformsPermittedCategoryMutation(string action)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        factory.Store.Owner.SetRole(UserRole.Administrator);
        var path = "/Categories/" + char.ToUpperInvariant(action[0]) + action[1..];
        var token = await ProgressHistoryTests.Token(client, action == "edit" ? path + "/" + factory.Store.Category.Id : action == "delete" ? "/Categories" : path);
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Id"] = factory.Store.Category.Id.ToString(), ["Name"] = "Managed category", ["Description"] = "Notes", ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        if (action == "delete") factory.Store.Category.IsDeleted.Should().BeTrue();
        else factory.Store.Categories.Values.Should().Contain(c => c.Name == "Managed category");
    }

    [Fact]
    public async Task Revocation_ImmediatelyDeniesPreviouslyAuthenticatedAdministratorAndRemovesUiControls()
    {
        using var factory = new SecurityWebApplicationFactory(); factory.Store.Owner.SetRole(UserRole.Administrator);
        using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, "/Categories/Create");
        (await client.GetStringAsync("/Categories")).Should().Contain("/Categories/Create");
        factory.Store.Owner.SetRole(UserRole.Regular); var writes = factory.Store.Writes;
        using var response = await client.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Name"] = "After revocation", ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden); factory.Store.Writes.Should().Be(writes);
        (await client.GetStringAsync("/Categories")).Should().NotContain("/Categories/Create");
        (await client.GetAsync("/Sessions/History")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrUnknownRole_RevokesExistingAdministratorCookie(bool unknown)
    {
        using var factory = new SecurityWebApplicationFactory(); factory.Store.Owner.SetRole(UserRole.Administrator);
        using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        if (unknown) typeof(StudyPlanner.Domain.Entities.User).GetProperty("Role")!.SetValue(factory.Store.Owner, (UserRole)999);
        else typeof(StudyPlanner.Domain.Entities.User).GetProperty("IsActive")!.SetValue(factory.Store.Owner, false);
        (await client.GetAsync("/Categories/Create")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task ProtectedButStaleAdministratorClaim_DoesNotOverrideAuthoritativeRegularRole()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, factory.Store.Owner.Id.ToString()), new Claim(ClaimTypes.Role, "Administrator") }, CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme);
        client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
        (await client.GetAsync("/Categories/Create")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrator_HasNoBypassToForeignStudyData()
    {
        using var factory = new SecurityWebApplicationFactory(); factory.Store.Owner.SetRole(UserRole.Administrator);
        using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        (await client.GetAsync("/StudyItems/Edit/" + factory.Store.ForeignItem.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/Sessions/Details/" + factory.Store.ForeignSession.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
