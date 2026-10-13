using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.UnitTests;

public class SeedStabilityTests
{
    [Fact]
    public void SeedData_MatchesHistoricalSnapshotAcrossUncachedModelBuilds()
    {
        using var first = Context(); using var second = Context();
        var firstModel = first.GetService<IDesignTimeModel>().Model; var secondModel = second.GetService<IDesignTimeModel>().Model;
        var snapshot = first.GetService<IModelRuntimeInitializer>().Initialize(first.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        foreach (var type in snapshot.GetEntityTypes().Where(t => t.GetSeedData().Any()))
        {
            var columns = type.GetProperties().Select(p => p.Name).ToHashSet();
            IEnumerable<Dictionary<string, object?>> Rows(IEntityType entity) => entity.GetSeedData(providerValues: true)
                .Select(r => columns.ToDictionary(k => k, k => r.TryGetValue(k, out var value) ? value : null)).OrderBy(r => r["Id"]);
            var expected = Rows(type).ToList();
            Rows(firstModel.FindEntityType(type.Name)!).Should().BeEquivalentTo(expected);
            Rows(secondModel.FindEntityType(type.Name)!).Should().BeEquivalentTo(expected);
        }
    }

    [Fact]
    public void StabilizedModel_RequiresNoSchemaOrDataMigrationOperations()
    {
        using var context = Context();
        var snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var model = context.GetService<IDesignTimeModel>().Model;
        context.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), model.GetRelationalModel()).Should().BeEmpty();
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=model-only-unused")
        .ReplaceService<IModelCacheKeyFactory, UncachedKeyFactory>().Options);
    private sealed class UncachedKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), Guid.NewGuid(), designTime);
    }
}
