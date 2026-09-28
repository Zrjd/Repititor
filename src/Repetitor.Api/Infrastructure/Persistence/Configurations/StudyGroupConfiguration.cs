using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence.Configurations;

public sealed class StudyGroupConfiguration : IEntityTypeConfiguration<StudyGroup>
{
    /// <summary>
    /// Настраивает сопоставление сущности StudyGroup с таблицей study_groups.
    /// Определяет название группы, её учителя и связи с участниками, курсами и приглашениями.
    /// </summary>
    public void Configure(EntityTypeBuilder<StudyGroup> b)
    {
        b.ToTable("study_groups");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.HasIndex(x => new { x.TeacherUserId, x.Name });
        b.HasOne(x => x.TeacherUser).WithMany()
            .HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StudyGroupMemberConfiguration : IEntityTypeConfiguration<StudyGroupMember>
{
    /// <summary>
    /// Настраивает сопоставление сущности StudyGroupMember с таблицей study_group_members.
    /// Ученик не может состоять в одной группе дважды — это гарантирует уникальный индекс.
    /// </summary>
    public void Configure(EntityTypeBuilder<StudyGroupMember> b)
    {
        b.ToTable("study_group_members");
        b.HasKey(x => x.Id);
        b.Property(x => x.JoinedAt).HasColumnType("timestamptz");
        b.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
        b.HasOne(x => x.Group).WithMany(x => x.Members)
            .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StudyGroupCourseConfiguration : IEntityTypeConfiguration<StudyGroupCourse>
{
    /// <summary>
    /// Настраивает сопоставление сущности StudyGroupCourse с таблицей study_group_courses.
    /// Один курс нельзя назначить одной группе дважды — это гарантирует уникальный индекс.
    /// </summary>
    public void Configure(EntityTypeBuilder<StudyGroupCourse> b)
    {
        b.ToTable("study_group_courses");
        b.HasKey(x => x.Id);
        b.Property(x => x.AssignedAt).HasColumnType("timestamptz");
        b.HasIndex(x => new { x.GroupId, x.CourseId }).IsUnique();
        b.HasOne(x => x.Group).WithMany(x => x.Courses)
            .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StudyGroupInvitationConfiguration : IEntityTypeConfiguration<StudyGroupInvitation>
{
    /// <summary>
    /// Настраивает сопоставление сущности StudyGroupInvitation с таблицей study_group_invitations.
    /// Код приглашения уникален, поэтому одинаковые коды в разных группах невозможны.
    /// </summary>
    public void Configure(EntityTypeBuilder<StudyGroupInvitation> b)
    {
        b.ToTable("study_group_invitations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(16).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => new { x.GroupId, x.IsRevoked });
        b.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.Group).WithMany(x => x.Invitations)
            .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
