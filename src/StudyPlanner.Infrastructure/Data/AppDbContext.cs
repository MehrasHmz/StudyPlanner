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
        // Fixed GUIDs so they match the hardcoded user ID in controllers
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var cat1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var cat2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var cat3 = Guid.Parse("10000000-0000-0000-0000-000000000003");

        mb.Entity<Category>().HasData(
            new Category(cat1, "Vocabulary", "Language learning"),
            new Category(cat2, "Programming", "Software development"),
            new Category(cat3, "Algorithms", "Data structures & algorithms"));

        mb.Entity<User>().HasData(new User(userId, "Demo User", "demo@studyplanner.com",
            Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("password123" + "StudyPlannerSalt2024")))));

        mb.Entity<StudyItem>().HasData(
            new StudyItem("Learn C# Basics", "Intro to C#", Domain.Enums.StudyItemType.Programming,
                Domain.Enums.DifficultyLevel.Beginner, 30, 4, cat2, userId, DateTime.UtcNow.AddDays(-2)),
            new StudyItem("Sorting Algorithms", "Implement quicksort", Domain.Enums.StudyItemType.Algorithm,
                Domain.Enums.DifficultyLevel.Intermediate, 45, 3, cat3, userId, DateTime.UtcNow.AddDays(-1)),
            new StudyItem("Spanish Greetings", "Basic greetings", Domain.Enums.StudyItemType.Vocabulary,
                Domain.Enums.DifficultyLevel.Beginner, 20, 2, cat1, userId, DateTime.UtcNow),
            new StudyItem("Design Patterns", "Study SOLID", Domain.Enums.StudyItemType.Programming,
                Domain.Enums.DifficultyLevel.Advanced, 60, 5, cat2, userId, DateTime.UtcNow.AddDays(1)));
    }
}
