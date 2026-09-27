using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence;

public sealed partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<GrammarTopic> GrammarTopics => Set<GrammarTopic>();
    public DbSet<CourseEnrollment> Enrollments => Set<CourseEnrollment>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<LexicalUnit> LexicalUnits => Set<LexicalUnit>();
    public DbSet<LexicalUnitEmbedding> LexicalUnitEmbeddings => Set<LexicalUnitEmbedding>();
    public DbSet<UserLexicalUnit> UserLexicalUnits => Set<UserLexicalUnit>();
    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<DeckCard> DeckCards => Set<DeckCard>();
    public DbSet<ReviewCard> ReviewCards => Set<ReviewCard>();
    public DbSet<ReviewLog> ReviewLogs => Set<ReviewLog>();
    public DbSet<UserDailyStat> UserDailyStats => Set<UserDailyStat>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseAttempt> ExerciseAttempts => Set<ExerciseAttempt>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PronunciationAttempt> PronunciationAttempts => Set<PronunciationAttempt>();
    public DbSet<AiCallLog> AiCallLogs => Set<AiCallLog>();

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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

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

    public static JsonNode? DeepClone(JsonNode? node) => node?.DeepClone();
}

public static class JsonNodeExtensions
{    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T? Deserialize<T>(this JsonNode? node) =>
        node is null ? default : node.Deserialize<T>(Options);

    public static JsonNode? ToNode<T>(this T value) => JsonSerializer.SerializeToNode(value, Options);

    public static JsonNode? CloneNode(this JsonNode? node) => node?.DeepClone();

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
