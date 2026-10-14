using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;

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
        b.Property(x => x.Role).HasConversion<int>().HasDefaultValue(UserRole.Regular).IsRequired();
        b.ToTable("Users", table => table.HasCheckConstraint("CK_Users_Role", "\"Role\" IN (0, 1)"));
    }
}
public class RoleChangeAuditConfig : IEntityTypeConfiguration<RoleChangeAudit>
{
    public void Configure(EntityTypeBuilder<RoleChangeAudit> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Operator).HasMaxLength(200).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.UserId, x.ChangedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("RoleChangeAudits", table => table.HasCheckConstraint("CK_RoleChangeAudits_Roles", "\"PreviousRole\" IN (0, 1) AND \"NewRole\" IN (0, 1)"));
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
        // Completion must still match the state originally loaded by this request.
        b.Property(x => x.Status).IsConcurrencyToken();
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
