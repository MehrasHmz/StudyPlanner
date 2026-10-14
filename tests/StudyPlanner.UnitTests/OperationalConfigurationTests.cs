using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace StudyPlanner.UnitTests;

public class OperationalConfigurationTests
{
    [Fact]
    public async Task ApprovedHosts_RejectUnknownHostWithoutExposingInternalDetails()
    {
        using var factory = new SecurityWebApplicationFactory { Settings = new Dictionary<string, string?> { ["AllowedHosts"] = "approved.example.test" } };
        using var client = factory.NewClient();
        (await client.GetAsync("/Auth/Login")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Host = "approved.example.test";
        (await client.GetAsync("/Auth/Login")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void KeyPerFile_MapsDoubleUnderscoreSecretsAndOverridesEarlierPlaceholderConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "studyplanner-secret-fixture-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "ConnectionStrings__DefaultConnection"), "synthetic-file-value");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "placeholder" })
                .AddKeyPerFile(directory, optional: false).Build();
            config.GetConnectionString("DefaultConnection").Should().Be("synthetic-file-value");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
