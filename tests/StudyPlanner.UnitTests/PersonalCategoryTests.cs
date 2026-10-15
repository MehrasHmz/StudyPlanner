using System.Net;
using FluentAssertions;
using FluentValidation;
using Moq;
using StudyPlanner.Application.Authorization;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.UnitTests;

public class PersonalCategoryTests
{
    [Fact]
    public async Task RegularUser_CreatesOwnedCategoryIgnoringOverpostedOwnerAndCanEditAndDeleteIt()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, "/Categories/Create");
        (await client.PostAsync("/Categories/Create", Form(token, "My category", Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var category = factory.Store.Categories.Values.Single(c => c.Name == "My category");
        category.UserId.Should().Be(factory.Store.Owner.Id);
        var html = await client.GetStringAsync("/Categories");
        html.Should().Contain("Personal").And.Contain("/Categories/Edit/" + category.Id).And.Contain("/Categories/Delete");
        token = await ProgressHistoryTests.Token(client, "/Categories/Edit/" + category.Id);
        (await client.PostAsync("/Categories/Edit", Form(token, "Renamed", category.Id))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        category.Name.Should().Be("Renamed"); category.UserId.Should().Be(factory.Store.Owner.Id);
        token = await ProgressHistoryTests.Token(client, "/Categories");
        (await client.PostAsync("/Categories/Delete", Form(token, "", category.Id))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        category.IsDeleted.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonalCategory_IsHiddenFromOtherUsersIncludingAdministrators(bool administrator)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        if (administrator) factory.Store.Owner.SetRole(UserRole.Administrator);
        var foreign = new Category("Private foreign category", "Secret notes", Guid.NewGuid());
        factory.Store.Categories.Add(foreign.Id, foreign);
        await ProgressHistoryTests.SignIn(client);
        (await client.GetStringAsync("/Categories")).Should().NotContain(foreign.Name).And.NotContain(foreign.Id.ToString());
        (await client.GetStringAsync("/api/categories/list")).Should().NotContain(foreign.Name).And.NotContain(foreign.Id.ToString());
        (await client.GetStringAsync("/StudyItems/Create?categoryId=" + foreign.Id)).Should().NotContain(foreign.Name).And.NotContain(foreign.Id.ToString());
        (await client.GetAsync("/Categories/Edit/" + foreign.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var token = await ProgressHistoryTests.Token(client, "/Categories"); var writes = factory.Store.Writes;
        foreach (var action in new[] { "Edit", "Delete" })
            (await client.PostAsync("/Categories/" + action, Form(token, "Hijacked", foreign.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Store.Writes.Should().Be(writes); foreign.Name.Should().Be("Private foreign category"); foreign.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    public async Task ForeignPersonalCategory_CannotBeAssignedToOwnedStudyItem(string action)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        var category = new Category("Foreign", "", Guid.NewGuid()); factory.Store.Categories.Add(category.Id, category);
        await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, action == "Edit" ? "/StudyItems/Edit/" + factory.Store.OwnItem.Id : "/StudyItems/Create");
        var writes = factory.Store.Writes;
        var fields = new Dictionary<string, string>
        {
            ["Id"] = factory.Store.OwnItem.Id.ToString(), ["Title"] = "Attempted", ["Description"] = "", ["CategoryId"] = category.Id.ToString(),
            ["Type"] = "Other", ["Difficulty"] = "Beginner", ["EstimatedDurationMinutes"] = "30", ["Priority"] = "3", ["__RequestVerificationToken"] = token
        };
        using var response = await client.PostAsync("/StudyItems/" + action, new FormUrlEncodedContent(fields));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Choose an available category");
        factory.Store.Writes.Should().Be(writes); factory.Store.OwnItem.CategoryId.Should().Be(factory.Store.Category.Id);
    }

    [Fact]
    public async Task DeletingReferencedPersonalCategory_ArchivesItWithoutDeletingStudyItems()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        var category = new Category("Personal used category", "", factory.Store.Owner.Id);
        factory.Store.Categories.Add(category.Id, category); factory.Store.OwnItem.UpdateCategory(category.Id);
        factory.Store.CategoryRepository.Setup(r => r.HasStudyItemsAsync(category.Id, Moq.It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, "/Categories");
        (await client.PostAsync("/Categories/Delete", Form(token, "", category.Id))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        category.IsDeleted.Should().BeTrue(); factory.Store.OwnItem.IsDeleted.Should().BeFalse();
        (await client.GetStringAsync("/Categories")).Should().NotContain(category.Name);
        (await client.GetAsync("/StudyItems/Edit/" + factory.Store.OwnItem.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NamesAreUniqueWithinPersonalLibraryButMayMatchSharedOrAnotherUsersNames()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, "/Categories/Create");
        var fields = Form(token, factory.Store.Category.Name, Guid.NewGuid());
        (await client.PostAsync("/Categories/Create", fields)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var writes = factory.Store.Writes;
        var response = await client.PostAsync("/Categories/Create", fields);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("A category with this name already exists");
        factory.Store.Writes.Should().Be(writes);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task DirectHandlers_RequireActiveOwnerAndDoNotGiveAdministratorsForeignAccess(string action)
    {
        using var factory = new SecurityWebApplicationFactory(); factory.Store.Owner.SetRole(UserRole.Administrator);
        var foreign = new Category("Foreign", "", Guid.NewGuid()); factory.Store.Categories.Add(foreign.Id, foreign);
        var permission = new CategoryPermission(new TestCurrentUser(factory.Store.Owner.Id), factory.Store.UserRepository.Object);
        if (action == "create") typeof(User).GetProperty("IsActive")!.SetValue(factory.Store.Owner, false);
        var writes = factory.Store.Writes;
        var error = await Record.ExceptionAsync(async () =>
        {
            if (action == "create") await new CreateCategoryHandler(factory.Store.CategoryRepository.Object, permission).Handle(new("New", ""), default);
            else if (action == "edit") await new UpdateCategoryHandler(factory.Store.CategoryRepository.Object, permission).Handle(new(foreign.Id, "Changed", ""), default);
            else await new DeleteCategoryHandler(factory.Store.CategoryRepository.Object, permission).Handle(new(foreign.Id), default);
        });
        if (action == "create") error.Should().BeOfType<CategoryManagementDeniedException>();
        else error.Should().BeOfType<KeyNotFoundException>();
        factory.Store.Writes.Should().Be(writes);
    }

    private static FormUrlEncodedContent Form(string token, string name, Guid id) => new(new Dictionary<string, string>
    { ["Id"] = id.ToString(), ["UserId"] = id.ToString(), ["OwnerUserId"] = id.ToString(), ["Name"] = name, ["Description"] = "", ["__RequestVerificationToken"] = token });
}
