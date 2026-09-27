using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence.Configurations;

public sealed class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> b)
    {
        b.ToTable("languages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(8).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.NameEnglish).HasMaxLength(80).IsRequired();
        b.Property(x => x.NameRussian).HasMaxLength(80).IsRequired();
        b.Property(x => x.NativeName).HasMaxLength(80);
        b.Property(x => x.FlagEmoji).HasMaxLength(16);
        b.Property(x => x.TtsVoiceHint).HasMaxLength(128);
    }
}

public sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> b)
    {
        b.ToTable("courses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.LanguageId, x.Slug }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Level).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.CoverUrl).HasMaxLength(512);
        b.Property(x => x.AccentColor).HasMaxLength(16);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.Language).WithMany()
            .HasForeignKey(x => x.LanguageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> b)
    {
        b.ToTable("lessons");
        b.HasKey(x => x.Id);
        b.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        b.HasIndex(x => new { x.CourseId, x.Slug }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(1000);
        b.Property(x => x.ContentMarkdown).HasColumnType("text");
        b.Property(x => x.KeyVocabulary).HasColumnType("text[]");
        b.HasOne(x => x.Course).WithMany(x => x.Lessons)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.GrammarTopic).WithMany()
            .HasForeignKey(x => x.GrammarTopicId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class GrammarTopicConfiguration : IEntityTypeConfiguration<GrammarTopic>
{
    public void Configure(EntityTypeBuilder<GrammarTopic> b)
    {
        b.ToTable("grammar_topics");
        b.HasKey(x => x.Id);
        b.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        b.HasIndex(x => new { x.LanguageId, x.Slug }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(1000);
        b.Property(x => x.ExplanationMarkdown).HasColumnType("text");
        b.Property(x => x.MinLevel).HasConversion<string>().HasMaxLength(8);
        b.HasOne(x => x.Language).WithMany()
            .HasForeignKey(x => x.LanguageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CourseEnrollmentConfiguration : IEntityTypeConfiguration<CourseEnrollment>
{
    public void Configure(EntityTypeBuilder<CourseEnrollment> b)
    {
        b.ToTable("course_enrollments");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.CourseId }).IsUnique();
        b.Property(x => x.EnrolledAt).HasColumnType("timestamptz");
        b.Property(x => x.CompletedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany(x => x.Enrollments)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> b)
    {
        b.ToTable("lesson_progress");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EnrollmentId, x.LessonId }).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.StartedAt).HasColumnType("timestamptz");
        b.Property(x => x.CompletedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.Enrollment).WithMany(x => x.LessonProgress)
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Lesson).WithMany()
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
    }
}
