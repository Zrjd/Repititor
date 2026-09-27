using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repetitor.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "ai_call_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EstimatedCostUsd = table.Column<double>(type: "double precision", precision: 12, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_call_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "languages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    NameEnglish = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NameRussian = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NativeName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FlagEmoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TtsVoiceHint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_languages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CoverUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_courses_languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "grammar_topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExplanationMarkdown = table.Column<string>(type: "text", nullable: true),
                    MinLevel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grammar_topics_languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lexical_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranslationLanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    NormalizedText = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Transcription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Translation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AlternativeTranslations = table.Column<string[]>(type: "text[]", nullable: true),
                    PartOfSpeech = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Gender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PluralForm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PastTense = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AudioUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExampleTarget = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExampleNative = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: true),
                    MinLearnerLevel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    FrequencyRank = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lexical_units", x => x.Id);
                    table.CheckConstraint("ck_lexical_units_fts_text_not_empty", "length(trim(\"Text\")) > 0");
                    table.ForeignKey(
                        name: "FK_lexical_units_languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lexical_units_languages_TranslationLanguageId",
                        column: x => x.TranslationLanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    InterfaceLanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetLanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TargetLevel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DailyGoalXp = table.Column<int>(type: "integer", nullable: false),
                    SpeechRate = table.Column<double>(type: "double precision", precision: 4, scale: 2, nullable: false),
                    PreferredAiProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreferredChatModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WantsCorrectionHints = table.Column<bool>(type: "boolean", nullable: false),
                    PushNotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TotalXp = table.Column<int>(type: "integer", nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false),
                    LastActivityDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    AiCallsToday = table.Column<int>(type: "integer", nullable: false),
                    AiCallsDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_languages_InterfaceLanguageId",
                        column: x => x.InterfaceLanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_users_languages_TargetLanguageId",
                        column: x => x.TargetLanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    GrammarTopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    KeyVocabulary = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lessons_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lessons_grammar_topics_GrammarTopicId",
                        column: x => x.GrammarTopicId,
                        principalTable: "grammar_topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "lexical_unit_embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LexicalUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lexical_unit_embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lexical_unit_embeddings_lexical_units_LexicalUnitId",
                        column: x => x.LexicalUnitId,
                        principalTable: "lexical_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Scenario = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SystemPromptOverride = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UseDictionaryContext = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    TotalInputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_course_enrollments_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_course_enrollments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LanguageCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CoverEmoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_decks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    SourceText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_assets_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedByRotation = table.Column<bool>(type: "boolean", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_daily_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    XpEarned = table.Column<int>(type: "integer", nullable: false),
                    ReviewsCompleted = table.Column<int>(type: "integer", nullable: false),
                    CorrectAnswers = table.Column<int>(type: "integer", nullable: false),
                    NewWordsLearned = table.Column<int>(type: "integer", nullable: false),
                    ExercisesCompleted = table.Column<int>(type: "integer", nullable: false),
                    ChatMessagesSent = table.Column<int>(type: "integer", nullable: false),
                    MinutesStudied = table.Column<int>(type: "integer", nullable: false),
                    GoalReached = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_daily_stats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_daily_stats_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranslationLanguageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Prompt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    ExplanationMarkdown = table.Column<string>(type: "text", nullable: true),
                    Level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Topics = table.Column<string[]>(type: "text[]", nullable: true),
                    TargetLexicalUnitIds = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    EstimatedSeconds = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AiProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AiConfidence = table.Column<double>(type: "double precision", precision: 5, scale: 4, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    CorrectRateBasisPoints = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exercises_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_exercises_languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exercises_lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_exercises_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    RagContextRefs = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    SourceLanguageCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    FeedbackRating = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "chat_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lesson_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    BestScorePercent = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lesson_progress_course_enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "course_enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lesson_progress_lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_lexical_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LexicalUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PersonalNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Suspended = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectStreak = table.Column<int>(type: "integer", nullable: false),
                    IncorrectStreak = table.Column<int>(type: "integer", nullable: false),
                    MasteryScore = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    LastDeckId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_lexical_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_lexical_units_decks_LastDeckId",
                        column: x => x.LastDeckId,
                        principalTable: "decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_lexical_units_lexical_units_LexicalUnitId",
                        column: x => x.LexicalUnitId,
                        principalTable: "lexical_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_lexical_units_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pronunciation_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    LexicalUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetText = table.Column<string>(type: "text", nullable: false),
                    Transcript = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RecognizedText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OverallScore = table.Column<int>(type: "integer", nullable: false),
                    AccuracyScore = table.Column<int>(type: "integer", nullable: false),
                    FluencyScore = table.Column<int>(type: "integer", nullable: false),
                    CompletenessScore = table.Column<int>(type: "integer", nullable: false),
                    ProsodyScore = table.Column<int>(type: "integer", nullable: false),
                    WordLevelScores = table.Column<string>(type: "jsonb", nullable: true),
                    FeedbackMarkdown = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    XpEarned = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pronunciation_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pronunciation_attempts_lexical_units_LexicalUnitId",
                        column: x => x.LexicalUnitId,
                        principalTable: "lexical_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pronunciation_attempts_media_assets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "media_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pronunciation_attempts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercise_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Answers = table.Column<string>(type: "jsonb", nullable: true),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    ScorePercent = table.Column<int>(type: "integer", nullable: false),
                    IsPassed = table.Column<bool>(type: "boolean", nullable: false),
                    XpEarned = table.Column<int>(type: "integer", nullable: false),
                    AiFeedbackMarkdown = table.Column<string>(type: "text", nullable: true),
                    AiProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exercise_attempts_exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_attempts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deck_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserLexicalUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsNew = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deck_cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deck_cards_decks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deck_cards_user_lexical_units_UserLexicalUnitId",
                        column: x => x.UserLexicalUnitId,
                        principalTable: "user_lexical_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserLexicalUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    IntervalDays = table.Column<double>(type: "double precision", precision: 8, scale: 3, nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", precision: 5, scale: 3, nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    Lapses = table.Column<int>(type: "integer", nullable: false),
                    LearningStep = table.Column<int>(type: "integer", nullable: false),
                    MaxIntervalDays = table.Column<int>(type: "integer", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    SuspendedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_cards_user_lexical_units_UserLexicalUnitId",
                        column: x => x.UserLexicalUnitId,
                        principalTable: "user_lexical_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    LexicalUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PreviousIntervalDays = table.Column<double>(type: "double precision", precision: 8, scale: 3, nullable: false),
                    NewIntervalDays = table.Column<double>(type: "double precision", precision: 8, scale: 3, nullable: false),
                    PreviousState = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    NewState = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    WasCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    GivenAnswer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_logs_lexical_units_LexicalUnitId",
                        column: x => x.LexicalUnitId,
                        principalTable: "lexical_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_review_logs_review_cards_ReviewCardId",
                        column: x => x.ReviewCardId,
                        principalTable: "review_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_call_logs_Operation",
                table: "ai_call_logs",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_ai_call_logs_UserId_CreatedAt",
                table: "ai_call_logs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_SessionId_CreatedAt",
                table: "chat_messages",
                columns: new[] { "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_sessions_UserId_LastMessageAt",
                table: "chat_sessions",
                columns: new[] { "UserId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_course_enrollments_CourseId",
                table: "course_enrollments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_course_enrollments_UserId_CourseId",
                table: "course_enrollments",
                columns: new[] { "UserId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_courses_LanguageId_Slug",
                table: "courses",
                columns: new[] { "LanguageId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deck_cards_DeckId_Position",
                table: "deck_cards",
                columns: new[] { "DeckId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_deck_cards_DeckId_UserLexicalUnitId",
                table: "deck_cards",
                columns: new[] { "DeckId", "UserLexicalUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deck_cards_UserLexicalUnitId",
                table: "deck_cards",
                column: "UserLexicalUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_decks_UserId_Name",
                table: "decks",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_attempts_ExerciseId_UserId",
                table: "exercise_attempts",
                columns: new[] { "ExerciseId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_attempts_UserId_CompletedAt",
                table: "exercise_attempts",
                columns: new[] { "UserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_exercises_CourseId",
                table: "exercises",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_LanguageId_Type_IsActive",
                table: "exercises",
                columns: new[] { "LanguageId", "Type", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_exercises_LessonId",
                table: "exercises",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_OwnerUserId_CreatedAt",
                table: "exercises",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_grammar_topics_LanguageId_Slug",
                table: "grammar_topics",
                columns: new[] { "LanguageId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_languages_Code",
                table: "languages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lesson_progress_EnrollmentId_LessonId",
                table: "lesson_progress",
                columns: new[] { "EnrollmentId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lesson_progress_LessonId",
                table: "lesson_progress",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_lessons_CourseId_Slug",
                table: "lessons",
                columns: new[] { "CourseId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lessons_GrammarTopicId",
                table: "lessons",
                column: "GrammarTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_lexical_unit_embeddings_LexicalUnitId_Provider_Model",
                table: "lexical_unit_embeddings",
                columns: new[] { "LexicalUnitId", "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lexical_units_ContentHash",
                table: "lexical_units",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_lexical_units_LanguageId_NormalizedText_TranslationLanguage~",
                table: "lexical_units",
                columns: new[] { "LanguageId", "NormalizedText", "TranslationLanguageId" },
                unique: true,
                filter: "\"Status\" <> 'Deprecated'");

            migrationBuilder.CreateIndex(
                name: "IX_lexical_units_LanguageId_Status",
                table: "lexical_units",
                columns: new[] { "LanguageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_lexical_units_NormalizedText",
                table: "lexical_units",
                column: "NormalizedText");

            migrationBuilder.CreateIndex(
                name: "IX_lexical_units_TranslationLanguageId",
                table: "lexical_units",
                column: "TranslationLanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_UserId_CreatedAt",
                table: "media_assets",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_TokenHash",
                table: "password_reset_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_UserId",
                table: "password_reset_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_pronunciation_attempts_LexicalUnitId",
                table: "pronunciation_attempts",
                column: "LexicalUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_pronunciation_attempts_MediaAssetId",
                table: "pronunciation_attempts",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_pronunciation_attempts_UserId_CreatedAt",
                table: "pronunciation_attempts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_RevokedAt",
                table: "refresh_tokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_review_cards_State_DueAt",
                table: "review_cards",
                columns: new[] { "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_review_cards_UserLexicalUnitId",
                table: "review_cards",
                column: "UserLexicalUnitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_logs_LexicalUnitId",
                table: "review_logs",
                column: "LexicalUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_review_logs_ReviewCardId",
                table: "review_logs",
                column: "ReviewCardId");

            migrationBuilder.CreateIndex(
                name: "IX_review_logs_UserId_ReviewedAt",
                table: "review_logs",
                columns: new[] { "UserId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_daily_stats_UserId_Date",
                table: "user_daily_stats",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_lexical_units_LastDeckId",
                table: "user_lexical_units",
                column: "LastDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_user_lexical_units_LexicalUnitId",
                table: "user_lexical_units",
                column: "LexicalUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_user_lexical_units_UserId_LexicalUnitId",
                table: "user_lexical_units",
                columns: new[] { "UserId", "LexicalUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_InterfaceLanguageId",
                table: "users",
                column: "InterfaceLanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_users_TargetLanguageId",
                table: "users",
                column: "TargetLanguageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_call_logs");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "deck_cards");

            migrationBuilder.DropTable(
                name: "exercise_attempts");

            migrationBuilder.DropTable(
                name: "lesson_progress");

            migrationBuilder.DropTable(
                name: "lexical_unit_embeddings");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "pronunciation_attempts");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "review_logs");

            migrationBuilder.DropTable(
                name: "user_daily_stats");

            migrationBuilder.DropTable(
                name: "chat_sessions");

            migrationBuilder.DropTable(
                name: "exercises");

            migrationBuilder.DropTable(
                name: "course_enrollments");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "review_cards");

            migrationBuilder.DropTable(
                name: "lessons");

            migrationBuilder.DropTable(
                name: "user_lexical_units");

            migrationBuilder.DropTable(
                name: "courses");

            migrationBuilder.DropTable(
                name: "grammar_topics");

            migrationBuilder.DropTable(
                name: "decks");

            migrationBuilder.DropTable(
                name: "lexical_units");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "languages");
        }
    }
}
