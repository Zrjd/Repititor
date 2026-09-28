using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence.Configurations;

public sealed class LexicalUnitConfiguration : IEntityTypeConfiguration<LexicalUnit>
{
    /// <summary>
    /// Настраивает сопоставление сущности LexicalUnit с таблицей lexical_units.
    /// Определяет ограничения длины полей, уникальные индексы и связи с языками, обеспечивая целостность словарных данных.
    /// </summary>
    public void Configure(EntityTypeBuilder<LexicalUnit> b)
    {
        b.ToTable("lexical_units", t =>
        {
            t.HasCheckConstraint("ck_lexical_units_fts_text_not_empty", "length(trim(\"Text\")) > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).HasMaxLength(400).IsRequired();
        b.Property(x => x.NormalizedText).HasMaxLength(400).IsRequired();
        b.Property(x => x.Transcription).HasMaxLength(200);
        b.Property(x => x.Translation).HasMaxLength(1000);
        b.Property(x => x.AlternativeTranslations).HasColumnType("text[]");
        b.Property(x => x.PartOfSpeech).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Gender).HasMaxLength(16);
        b.Property(x => x.PluralForm).HasMaxLength(200);
        b.Property(x => x.PastTense).HasMaxLength(200);
        b.Property(x => x.AudioUrl).HasMaxLength(512);
        b.Property(x => x.ExampleTarget).HasMaxLength(1000);
        b.Property(x => x.ExampleNative).HasMaxLength(1000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.Tags).HasColumnType("text[]");
        b.Property(x => x.MinLearnerLevel).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.ContentHash).HasMaxLength(64);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");

        b.HasIndex(x => new { x.LanguageId, x.NormalizedText, x.TranslationLanguageId })
            .IsUnique()
            .HasFilter("\"Status\" <> 'Deprecated'");
        b.HasIndex(x => x.NormalizedText);
        b.HasIndex(x => new { x.LanguageId, x.Status });
        b.HasIndex(x => x.ContentHash);

        b.HasOne(x => x.Language).WithMany()
            .HasForeignKey(x => x.LanguageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TranslationLanguage).WithMany()
            .HasForeignKey(x => x.TranslationLanguageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LexicalUnitEmbeddingConfiguration : IEntityTypeConfiguration<LexicalUnitEmbedding>
{
    /// <summary>
    /// Настраивает сопоставление сущности LexicalUnitEmbedding с таблицей lexical_unit_embeddings.
    /// Определяет параметры хранения векторных представлений словарных единиц, включая уникальный индекс по паре единица-провайдер-модель.
    /// </summary>
    public void Configure(EntityTypeBuilder<LexicalUnitEmbedding> b)
    {
        b.ToTable("lexical_unit_embeddings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        b.Property(x => x.Model).HasMaxLength(128).IsRequired();
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        b.HasIndex(x => new { x.LexicalUnitId, x.Provider, x.Model }).IsUnique();
        b.HasOne(x => x.LexicalUnit).WithMany(x => x.Embeddings)
            .HasForeignKey(x => x.LexicalUnitId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserLexicalUnitConfiguration : IEntityTypeConfiguration<UserLexicalUnit>
{
    /// <summary>
    /// Настраивает сопоставление сущности UserLexicalUnit с таблицей user_lexical_units.
    /// Определяет связь пользователя с его словарными единицами, включая состояние изучения и персональные заметки.
    /// </summary>
    public void Configure(EntityTypeBuilder<UserLexicalUnit> b)
    {
        b.ToTable("user_lexical_units");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.LexicalUnitId }).IsUnique();
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.PersonalNote).HasMaxLength(2000);
        b.Property(x => x.AddedAt).HasColumnType("timestamptz");
        b.Property(x => x.LastReviewedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany(x => x.LexicalUnits)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.LexicalUnit).WithMany()
            .HasForeignKey(x => x.LexicalUnitId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.LastDeck).WithMany()
            .HasForeignKey(x => x.LastDeckId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DeckConfiguration : IEntityTypeConfiguration<Deck>
{
    /// <summary>
    /// Настраивает сопоставление сущности Deck с таблицей decks.
    /// Определяет параметры колод карточек, включая название, описание и связь с пользователем-владельцем.
    /// </summary>
    public void Configure(EntityTypeBuilder<Deck> b)
    {
        b.ToTable("decks");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.Name });
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.LanguageCode).HasMaxLength(8);
        b.Property(x => x.CoverEmoji).HasMaxLength(16);
        b.Property(x => x.Tags).HasColumnType("text[]");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany(x => x.Decks)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeckCardConfiguration : IEntityTypeConfiguration<DeckCard>
{
    /// <summary>
    /// Настраивает сопоставление сущности DeckCard с таблицей deck_cards.
    /// Определяет связь карточек с колодами и словарными единицами пользователя, включая позицию в колоде.
    /// </summary>
    public void Configure(EntityTypeBuilder<DeckCard> b)
    {
        b.ToTable("deck_cards");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.DeckId, x.UserLexicalUnitId }).IsUnique();
        b.HasIndex(x => new { x.DeckId, x.Position });
        b.Property(x => x.AddedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.Deck).WithMany(x => x.Cards)
            .HasForeignKey(x => x.DeckId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.UserLexicalUnit).WithMany()
            .HasForeignKey(x => x.UserLexicalUnitId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReviewCardConfiguration : IEntityTypeConfiguration<ReviewCard>
{
    /// <summary>
    /// Настраивает сопоставление сущности ReviewCard с таблицей review_cards.
    /// Определяет параметры карточек для интервального повторения, включая состояние, интервал и коэффициент лёгкости.
    /// </summary>
    public void Configure(EntityTypeBuilder<ReviewCard> b)
    {
        b.ToTable("review_cards");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserLexicalUnitId).IsUnique();
        b.HasIndex(x => new { x.State, x.DueAt });
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.DueAt).HasColumnType("timestamptz");
        b.Property(x => x.IntervalDays).HasPrecision(8, 3);
        b.Property(x => x.EaseFactor).HasPrecision(5, 3);
        b.Property(x => x.LastReviewedAt).HasColumnType("timestamptz");
        b.Property(x => x.SuspendedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.UserLexicalUnit).WithOne(x => x.ReviewCard)
            .HasForeignKey<ReviewCard>(x => x.UserLexicalUnitId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReviewLogConfiguration : IEntityTypeConfiguration<ReviewLog>
{
    /// <summary>
    /// Настраивает сопоставление сущности ReviewLog с таблицей review_logs.
    /// Определяет параметры журнала повторений, включая оценку, предыдущий и новый интервалы, а также связь с карточкой повторения.
    /// </summary>
    public void Configure(EntityTypeBuilder<ReviewLog> b)
    {
        b.ToTable("review_logs");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.ReviewedAt });
        b.HasIndex(x => x.ReviewCardId);
        b.Property(x => x.Rating).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.PreviousIntervalDays).HasPrecision(8, 3);
        b.Property(x => x.NewIntervalDays).HasPrecision(8, 3);
        b.Property(x => x.PreviousState).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.NewState).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.GivenAnswer).HasMaxLength(1000);
        b.Property(x => x.ReviewedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.ReviewCard).WithMany()
            .HasForeignKey(x => x.ReviewCardId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.LexicalUnit).WithMany()
            .HasForeignKey(x => x.LexicalUnitId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserDailyStatConfiguration : IEntityTypeConfiguration<UserDailyStat>
{
    /// <summary>
    /// Настраивает сопоставление сущности UserDailyStat с таблицей user_daily_stats.
    /// Определяет параметры ежедневной статистики пользователя с уникальным индексом по паре пользователь-дата.
    /// </summary>
    public void Configure(EntityTypeBuilder<UserDailyStat> b)
    {
        b.ToTable("user_daily_stats");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
        b.Property(x => x.Date).HasColumnType("date");
        b.HasOne(x => x.User).WithMany(x => x.DailyStats)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
