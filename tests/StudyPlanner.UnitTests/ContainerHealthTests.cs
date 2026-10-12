using System.Net;
using FluentAssertions;

namespace StudyPlanner.UnitTests;

public class ContainerHealthTests
{
    [Fact]
    public async Task Liveness_IsPublicAndDoesNotRequireDatabaseOrAuthentication()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("healthy");
    }

    [Fact]
    public async Task Readiness_WithoutDatabase_Returns503WithoutConnectionDetails()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Password").And.NotContain("Exception");
    }
}
