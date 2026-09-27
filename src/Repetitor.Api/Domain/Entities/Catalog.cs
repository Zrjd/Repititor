using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Domain.Entities;

public sealed class Language
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string NameEnglish { get; set; } = string.Empty;
    public string NameRussian { get; set; } = string.Empty;
    public string? NativeName { get; set; }
    public string? FlagEmoji { get; set; }
    public string? TtsVoiceHint { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class Course
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LanguageId { get; set; }
    public Language? Language { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    public string? CoverUrl { get; set; }
    public string? AccentColor { get; set; }
    public int EstimatedMinutes { get; set; } = 60;
    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Lesson> Lessons { get; set; } = [];
}

public sealed class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ContentMarkdown { get; set; }
    public int SortOrder { get; set; }
    public int EstimatedMinutes { get; set; } = 10;
    public bool IsPublished { get; set; } = true;
    public Guid? GrammarTopicId { get; set; }
    public GrammarTopic? GrammarTopic { get; set; }
    public string[]? KeyVocabulary { get; set; }
}

public sealed class GrammarTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LanguageId { get; set; }
    public Language? Language { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ExplanationMarkdown { get; set; }
    public CefrLevel MinLevel { get; set; } = CefrLevel.A1;
    public bool IsPublished { get; set; } = true;
}

public sealed class CourseEnrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<LessonProgress> LessonProgress { get; set; } = [];
}

public sealed class LessonProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnrollmentId { get; set; }
    public CourseEnrollment? Enrollment { get; set; }
    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public LessonProgressStatus Status { get; set; } = LessonProgressStatus.NotStarted;
    public int ProgressPercent { get; set; }
    public int BestScorePercent { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
