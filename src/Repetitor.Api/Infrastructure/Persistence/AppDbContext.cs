using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence;

public sealed partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>Коллекция пользователей системы.</summary>
    public DbSet<User> Users => Set<User>();
    /// <summary>Коллекция refresh-токенов для обновления сессий.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    /// <summary>Коллекция токенов для сброса пароля.</summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    /// <summary>Коллекция поддерживаемых языков.</summary>
    public DbSet<Language> Languages => Set<Language>();
    /// <summary>Коллекция курсов.</summary>
    public DbSet<Course> Courses => Set<Course>();
    /// <summary>Коллекция уроков в курсах.</summary>
    public DbSet<Lesson> Lessons => Set<Lesson>();
    /// <summary>Коллекция тем грамматики.</summary>
    public DbSet<GrammarTopic> GrammarTopics => Set<GrammarTopic>();
    /// <summary>Коллекция записей пользователей на курсы.</summary>
    public DbSet<CourseEnrollment> Enrollments => Set<CourseEnrollment>();
    /// <summary>Коллекция прогресса прохождения уроков.</summary>
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    /// <summary>Коллекция словарных единиц (слов и фраз).</summary>
    public DbSet<LexicalUnit> LexicalUnits => Set<LexicalUnit>();
    /// <summary>Коллекция векторных представлений (эмбеддингов) словарных единиц.</summary>
    public DbSet<LexicalUnitEmbedding> LexicalUnitEmbeddings => Set<LexicalUnitEmbedding>();
    /// <summary>Коллекция связей пользователь ↔ словарная единица (изучение слов).</summary>
    public DbSet<UserLexicalUnit> UserLexicalUnits => Set<UserLexicalUnit>();
    /// <summary>Коллекция колод карточек для повторения.</summary>
    public DbSet<Deck> Decks => Set<Deck>();
    /// <summary>Коллекция карточек внутри колод.</summary>
    public DbSet<DeckCard> DeckCards => Set<DeckCard>();
    /// <summary>Коллекция карточек для интервального повторения (SRS).</summary>
    public DbSet<ReviewCard> ReviewCards => Set<ReviewCard>();
    /// <summary>Коллекция записей о повторениях (история).</summary>
    public DbSet<ReviewLog> ReviewLogs => Set<ReviewLog>();
    /// <summary>Коллекция ежедневной статистики пользователей.</summary>
    public DbSet<UserDailyStat> UserDailyStats => Set<UserDailyStat>();
    /// <summary>Коллекция упражнений.</summary>
    public DbSet<Exercise> Exercises => Set<Exercise>();
    /// <summary>Коллекция попыток выполнения упражнений.</summary>
    public DbSet<ExerciseAttempt> ExerciseAttempts => Set<ExerciseAttempt>();
    /// <summary>Коллекция чат-сессий с ИИ-репетитором.</summary>
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    /// <summary>Коллекция сообщений в чат-сессиях.</summary>
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    /// <summary>Коллекция медиа-файлов (изображения, аудио).</summary>
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    /// <summary>Коллекция попыток произношения (упражнения на произношение).</summary>
    public DbSet<PronunciationAttempt> PronunciationAttempts => Set<PronunciationAttempt>();
    /// <summary>Коллекция записей об обращениях к ИИ-сервисам.</summary>
    public DbSet<AiCallLog> AiCallLogs => Set<AiCallLog>();

    /// <summary>
    /// Единые настройки JSON-сериализации для всех операций с JSON в БД.
    /// Используются при сериализации в jsonb-колонки и десериализации обратно, чтобы формат был консистентным.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Entity<ReviewLog>().Property(x => x.ReviewedAt).HasColumnType("timestamptz");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<JsonNode>()
            .HaveConversion<JsonNodeValueConverter>()
            .HaveColumnType("jsonb");
    }

    /// <summary>
    /// Синхронно сохраняет изменения в БД, предварительно обновляя временные метки CreatedAt и UpdatedAt.
    /// Переопределение нужно для автоматической простановки дат создания/изменения сущностей перед записью.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Асинхронно сохраняет изменения в БД, предварительно обновляя временные метки CreatedAt и UpdatedAt.
    /// Асинхронная версия SaveChanges для неблокирующей работы с базой данных.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.CurrentValues["UpdatedAt"] = now;
            }

            if (entry.Metadata.FindProperty("CreatedAt") is not null && entry.State == EntityState.Added)
            {
                entry.CurrentValues["CreatedAt"] = now;
            }
        }
    }

    /// <summary>
    /// Создаёт глубокую копию JSON-узла. Нужна для безопасного копирования JSON-объектов без ссылок на оригинал.
    /// Возвращает null, если входной узел равен null.
    /// </summary>
    public static JsonNode? DeepClone(JsonNode? node) => node?.DeepClone();
}

/// <summary>
/// Набор вспомогательных методов для работы с JSON-узлами и трекером изменений EF Core.
/// </summary>
public static class JsonNodeExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Десериализует JSON-узел в объект указанного типа. Возвращает значение по умолчанию, если узел равен null.
    /// </summary>
    public static T? Deserialize<T>(this JsonNode? node) =>
        node is null ? default : node.Deserialize<T>(Options);

    /// <summary>
    /// Сериализует произвольный объект в JSON-узел. Удобно для помещения данных в jsonb-колонки.
    /// </summary>
    public static JsonNode? ToNode<T>(this T value) => JsonSerializer.SerializeToNode(value, Options);

    /// <summary>
    /// Создаёт глубокую копию JSON-узла, отвязанную от оригинала.
    /// </summary>
    public static JsonNode? CloneNode(this JsonNode? node) => node?.DeepClone();

    /// <summary>
    /// Сбрасывает все отслеживаемые сущности трекера в состояние Detached.
    /// Нужна для освобождения памяти и предотвращения утечек при длительных операциях.
    /// </summary>
    public static ChangeTracker ClearTracked(this ChangeTracker tracker)
    {
        tracker.DetectChanges();
        foreach (var entry in tracker.Entries().ToList())
        {
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }

        return tracker;
    }
}
