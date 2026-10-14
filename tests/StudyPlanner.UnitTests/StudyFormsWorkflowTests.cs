using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.UnitTests;

public class StudyFormsWorkflowTests
{
    [Theory]
    [InlineData("Create", "Type", "99")]
    [InlineData("Create", "Difficulty", "99")]
    [InlineData("Create", "NextReviewDate", "not-a-date")]
    [InlineData("Edit", "Type", "99")]
    [InlineData("Edit", "Difficulty", "99")]
    [InlineData("Edit", "EstimatedDurationMinutes", "not-a-number")]
    public async Task InvalidStudySettings_RenderErrorsWithoutWrites(string action, string field, string value)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var values = StudyValues(factory); values[field] = value;
        var path = "/StudyItems/" + action;
        values["__RequestVerificationToken"] = await Token(client, action == "Edit" ? path + "/" + factory.Store.OwnItem.Id : path);
        var writes = factory.Store.Writes;
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(values));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("validation-summary-errors").And.Contain("name=\"Title\"");
        factory.Store.Writes.Should().Be(writes);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    public async Task StudyDescription_IsReallyOptional(string action)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var values = StudyValues(factory); values["Description"] = "";
        var path = "/StudyItems/" + action;
        values["__RequestVerificationToken"] = await Token(client, action == "Edit" ? path + "/" + factory.Store.OwnItem.Id : path);
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(values));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Store.Items.Values.Should().Contain(item => item.Title == "Preserved study title" && item.Description == "");
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    public async Task CategoryDescription_IsReallyOptional(string action)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        factory.Store.Owner.SetRole(UserRole.Administrator);
        var path = "/Categories/" + action;
        var values = new Dictionary<string, string> { ["Id"] = factory.Store.Category.Id.ToString(), ["Name"] = "Networks", ["Description"] = "" };
        values["__RequestVerificationToken"] = await Token(client, action == "Edit" ? path + "/" + factory.Store.Category.Id : path);
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(values));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Store.Categories.Values.Should().Contain(category => category.Name == "Networks" && category.Description == "");
    }

    [Fact]
    public async Task CategoryEdit_EnforcesLimitsAndPreservesAttemptedInput()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        factory.Store.Owner.SetRole(UserRole.Administrator);
        var token = await Token(client, "/Categories/Edit/" + factory.Store.Category.Id);
        var writes = factory.Store.Writes;
        using var response = await client.PostAsync("/Categories/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = factory.Store.Category.Id.ToString(), ["Name"] = "Attempted category name", ["Description"] = new string('x', 501), ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("validation-summary-errors").And.Contain("Attempted category name");
        factory.Store.Category.Name.Should().Be("Test category"); factory.Store.Writes.Should().Be(writes);
    }

    [Fact]
    public async Task CategoryEdit_IsNotLimitedToFirstHundredAndRejectsDeletedCategories()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        factory.Store.Owner.SetRole(UserRole.Administrator);
        for (var index = 0; index < 105; index++) { var category = new Category("Category " + index); factory.Store.Categories.Add(category.Id, category); }
        var last = factory.Store.Categories.Values.Last();
        using var response = await client.GetAsync("/Categories/Edit/" + last.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createResponse = await client.GetAsync("/StudyItems/Create?categoryId=" + last.Id);
        (await createResponse.Content.ReadAsStringAsync()).Should().Contain("value=\"" + last.Id + "\" selected=\"selected\"");
        last.SoftDelete();
        using var deletedResponse = await client.GetAsync("/Categories/Edit/" + last.Id);
        deletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var indexResponse = await client.GetAsync("/Categories");
        (await indexResponse.Content.ReadAsStringAsync()).Should().Contain("Category pages").And.Contain("Page 1 of");
    }

    [Fact]
    public async Task CategoryEntry_PreselectsExistingCategoryOnly()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        using var response = await client.GetAsync("/StudyItems/Create?categoryId=" + factory.Store.Category.Id);
        (await response.Content.ReadAsStringAsync()).Should().Contain("value=\"" + factory.Store.Category.Id + "\" selected=\"selected\"");
        using var missingResponse = await client.GetAsync("/StudyItems/Create?categoryId=" + Guid.NewGuid());
        missingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Dictionary<string, string> StudyValues(SecurityWebApplicationFactory factory) => new()
    {
        ["Id"] = factory.Store.OwnItem.Id.ToString(), ["Title"] = "Preserved study title", ["Description"] = "Notes", ["Type"] = "Other",
        ["Difficulty"] = "Beginner", ["Priority"] = "3", ["EstimatedDurationMinutes"] = "30", ["CategoryId"] = factory.Store.Category.Id.ToString()
    };
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
        using var response = await client.GetAsync(path); response.StatusCode.Should().Be(HttpStatusCode.OK);
        var match = Regex.Match(await response.Content.ReadAsStringAsync(), "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue(); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
