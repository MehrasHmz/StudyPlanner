using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Infrastructure.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<StudyItem> StudyItems => Set<StudyItem>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<StudySessionItem> StudySessionItems => Set<StudySessionItem>();
    public DbSet<ProgressRecord> ProgressRecords => Set<ProgressRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                        v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v,
                        v => v));
                }
            }
        }

        SeedData(modelBuilder);
    }
    private static void SeedData(ModelBuilder mb)
    {
        // Match existing migration seed identities/timestamps; never regenerate user data in future migrations.
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var cat1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var cat2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var cat3 = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var baseline = new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc);
        mb.Entity<Category>().HasData(new Category(cat1, "Vocabulary", "Language learning"),
            new Category(cat2, "Programming", "Software development"), new Category(cat3, "Algorithms", "Data structures & algorithms"));
        mb.Entity<User>().HasData(new
        {
            Id = userId, FullName = "Demo User", Email = "demo@studyplanner.com", IsActive = true, Role = Domain.Enums.UserRole.Regular,
            PasswordHash = "ijB+qBaQa257yaJeoHCrzR/fBHVJ4IHm+mpkiCk7GY0=",
            CreatedAt = baseline.AddTicks(1670), UpdatedAt = (DateTime?)null
        });
        mb.Entity<StudyItem>().HasData(
            new { Id = Guid.Parse("e2224df1-5f86-438e-8e2b-0d9253495856"), Title = "Learn C# Basics", Description = "Intro to C#",
                Type = Domain.Enums.StudyItemType.Programming, Difficulty = Domain.Enums.DifficultyLevel.Beginner,
                EstimatedDurationMinutes = 30, Priority = 4, CategoryId = cat2, UserId = userId,
                NextReviewDate = baseline.AddDays(-2).AddTicks(1700), CreatedAt = baseline.AddTicks(1730), UpdatedAt = (DateTime?)null, IsCompleted = false, IsDeleted = false },
            new { Id = Guid.Parse("4ed4ecfa-0ad7-4fbc-a56d-8e7f4e4d87c5"), Title = "Sorting Algorithms", Description = "Implement quicksort",
                Type = Domain.Enums.StudyItemType.Algorithm, Difficulty = Domain.Enums.DifficultyLevel.Intermediate,
                EstimatedDurationMinutes = 45, Priority = 3, CategoryId = cat3, UserId = userId,
                NextReviewDate = baseline.AddDays(-1).AddTicks(1740), CreatedAt = baseline.AddTicks(1740), UpdatedAt = (DateTime?)null, IsCompleted = false, IsDeleted = false },
            new { Id = Guid.Parse("fa3d4c12-40fa-4936-922d-961311e628db"), Title = "Spanish Greetings", Description = "Basic greetings",
                Type = Domain.Enums.StudyItemType.Vocabulary, Difficulty = Domain.Enums.DifficultyLevel.Beginner,
                EstimatedDurationMinutes = 20, Priority = 2, CategoryId = cat1, UserId = userId,
                NextReviewDate = baseline.AddTicks(1740), CreatedAt = baseline.AddTicks(1750), UpdatedAt = (DateTime?)null, IsCompleted = false, IsDeleted = false },
            new { Id = Guid.Parse("06fac01c-1184-4e0d-bce1-b068e0f8dd81"), Title = "Design Patterns", Description = "Study SOLID",
                Type = Domain.Enums.StudyItemType.Programming, Difficulty = Domain.Enums.DifficultyLevel.Advanced,
                EstimatedDurationMinutes = 60, Priority = 5, CategoryId = cat2, UserId = userId,
                NextReviewDate = baseline.AddDays(1).AddTicks(1750), CreatedAt = baseline.AddTicks(1750), UpdatedAt = (DateTime?)null, IsCompleted = false, IsDeleted = false });
    }
}
