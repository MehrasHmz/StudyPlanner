using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Infrastructure.Configurations;
public class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Email).IsRequired().HasMaxLength(300);
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PasswordHash).IsRequired().HasMaxLength(500);
    }
}
public class CategoryConfig : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
public class StudyItemConfig : IEntityTypeConfiguration<StudyItem>
{
    public void Configure(EntityTypeBuilder<StudyItem> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasOne(x => x.Category).WithMany(c => c.StudyItems).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.User).WithMany(u => u.StudyItems).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
public class StudySessionConfig : IEntityTypeConfiguration<StudySession>
{
    public void Configure(EntityTypeBuilder<StudySession> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.User).WithMany(u => u.Sessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class StudySessionItemConfig : IEntityTypeConfiguration<StudySessionItem>
{
    public void Configure(EntityTypeBuilder<StudySessionItem> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.StudySession).WithMany(s => s.SessionItems).HasForeignKey(x => x.StudySessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.StudyItem).WithMany().HasForeignKey(x => x.StudyItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class ProgressRecordConfig : IEntityTypeConfiguration<ProgressRecord>
{
    public void Configure(EntityTypeBuilder<ProgressRecord> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.User).WithMany(u => u.ProgressRecords).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.StudyItem).WithMany().HasForeignKey(x => x.StudyItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
