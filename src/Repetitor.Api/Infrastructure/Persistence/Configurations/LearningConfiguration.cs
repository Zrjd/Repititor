using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence.Configurations;

public sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    /// <summary>
    /// Настраивает сопоставление сущности Exercise с таблицей exercises.
    /// Определяет параметры упражнений, включая тип, уровень, темы и связи с курсами и уроками.
    /// </summary>
    public void Configure(EntityTypeBuilder<Exercise> b)
    {
        b.ToTable("exercises");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Instructions).HasMaxLength(2000);
        b.Property(x => x.Prompt).HasMaxLength(2000);
        b.Property(x => x.ExplanationMarkdown).HasColumnType("text");
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Level).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Topics).HasColumnType("text[]");
        b.Property(x => x.TargetLexicalUnitIds).HasColumnType("uuid[]");
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.AiProvider).HasMaxLength(64);
        b.Property(x => x.AiModel).HasMaxLength(128);
        b.Property(x => x.AiConfidence).HasPrecision(5, 4);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.LastUsedAt).HasColumnType("timestamptz");

        b.HasIndex(x => new { x.LanguageId, x.Type, x.IsActive });
        b.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });
        b.HasIndex(x => x.CourseId);
        b.HasIndex(x => x.LessonId);

        b.HasOne(x => x.OwnerUser).WithMany()
            .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Lesson).WithMany()
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Language).WithMany()
            .HasForeignKey(x => x.LanguageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExerciseAttemptConfiguration : IEntityTypeConfiguration<ExerciseAttempt>
{
    /// <summary>
    /// Настраивает сопоставление сущности ExerciseAttempt с таблицей exercise_attempts.
    /// Определяет параметры попыток выполнения упражнений, включая связь с упражнением и пользователем.
    /// </summary>
    public void Configure(EntityTypeBuilder<ExerciseAttempt> b)
    {
        b.ToTable("exercise_attempts");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.CompletedAt });
        b.HasIndex(x => new { x.ExerciseId, x.UserId });
        b.Property(x => x.AiFeedbackMarkdown).HasColumnType("text");
        b.Property(x => x.AiProvider).HasMaxLength(64);
        b.Property(x => x.AiModel).HasMaxLength(128);
        b.Property(x => x.StartedAt).HasColumnType("timestamptz");
        b.Property(x => x.CompletedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.Exercise).WithMany()
            .HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    /// <summary>
    /// Настраивает сопоставление сущности ChatSession с таблицей chat_sessions.
    /// Определяет параметры чат-сессий с ИИ, включая режим, уровень и системный промпт.
    /// </summary>
    public void Configure(EntityTypeBuilder<ChatSession> b)
    {
        b.ToTable("chat_sessions");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.LastMessageAt });
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Level).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Scenario).HasMaxLength(2000);
        b.Property(x => x.SystemPromptOverride).HasMaxLength(8000);
        b.Property(x => x.Provider).HasMaxLength(64);
        b.Property(x => x.Model).HasMaxLength(128);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.LastMessageAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    /// <summary>
    /// Настраивает сопоставление сущности ChatMessage с таблицей chat_messages.
    /// Определяет параметры сообщений чата, включая содержимое, аудио и ссылки на контекст RAG.
    /// </summary>
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("chat_messages");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.SessionId, x.CreatedAt });
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.AudioUrl).HasMaxLength(512);
        b.Property(x => x.Provider).HasMaxLength(64);
        b.Property(x => x.Model).HasMaxLength(128);
        b.Property(x => x.RagContextRefs).HasColumnType("uuid[]");
        b.Property(x => x.SourceLanguageCode).HasMaxLength(8);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.Session).WithMany(x => x.Messages)
            .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    /// <summary>
    /// Настраивает сопоставление сущности MediaAsset с таблицей media_assets.
    /// Определяет параметры медиа-файлов, включая путь хранения, тип содержимого и срок действия.
    /// </summary>
    public void Configure(EntityTypeBuilder<MediaAsset> b)
    {
        b.ToTable("media_assets");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Property(x => x.StoragePath).HasMaxLength(600).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        b.Property(x => x.SourceText).HasMaxLength(4000);
        b.Property(x => x.Provider).HasMaxLength(64);
        b.Property(x => x.Model).HasMaxLength(128);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PronunciationAttemptConfiguration : IEntityTypeConfiguration<PronunciationAttempt>
{
    /// <summary>
    /// Настраивает сопоставление сущности PronunciationAttempt с таблицей pronunciation_attempts.
    /// Определяет параметры попыток произношения, включая распознанный текст и связь с медиа-файлами.
    /// </summary>
    public void Configure(EntityTypeBuilder<PronunciationAttempt> b)
    {
        b.ToTable("pronunciation_attempts");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Property(x => x.TargetText).IsRequired();
        b.Property(x => x.Transcript).HasMaxLength(4000);
        b.Property(x => x.RecognizedText).HasMaxLength(4000);
        b.Property(x => x.FeedbackMarkdown).HasColumnType("text");
        b.Property(x => x.Provider).HasMaxLength(64);
        b.Property(x => x.Model).HasMaxLength(128);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.MediaAsset).WithMany()
            .HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.LexicalUnit).WithMany()
            .HasForeignKey(x => x.LexicalUnitId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AiCallLogConfiguration : IEntityTypeConfiguration<AiCallLog>
{
    /// <summary>
    /// Настраивает сопоставление сущности AiCallLog с таблицей ai_call_logs.
    /// Определяет параметры журнала вызовов ИИ, включая провайдера, модель и оценку стоимости.
    /// </summary>
    public void Configure(EntityTypeBuilder<AiCallLog> b)
    {
        b.ToTable("ai_call_logs");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => x.Operation);
        b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        b.Property(x => x.Model).HasMaxLength(128).IsRequired();
        b.Property(x => x.Operation).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ErrorCode).HasMaxLength(64);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.EstimatedCostUsd).HasPrecision(12, 6);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
    }
}
