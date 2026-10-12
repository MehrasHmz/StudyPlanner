using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Infrastructure.Data;
using Xunit.Abstractions;

namespace StudyPlanner.UnitTests;

public class DependencySecurityTests
{
    private readonly ITestOutputHelper _output;
    public DependencySecurityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void PatchedDependencies_LoadAndPreserveSessionConcurrencyMapping()
    {
        foreach (var assembly in new[] { typeof(NpgsqlConnection).Assembly, typeof(MemoryCache).Assembly,
                     typeof(System.Text.Json.JsonSerializer).Assembly, typeof(DbContext).Assembly })
        {
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            _output.WriteLine($"{assembly.GetName().Name}: {version}; {assembly.Location}; file {FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion}");
        }
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dependency_model_only").Options);
        context.Model.FindEntityType(typeof(StudySession))!.FindProperty(nameof(StudySession.Status))!
            .IsConcurrencyToken.Should().BeTrue();
        typeof(NpgsqlConnection).Assembly.GetName().Version.Should().BeGreaterThanOrEqualTo(new Version(8, 0, 6));
    }
}
