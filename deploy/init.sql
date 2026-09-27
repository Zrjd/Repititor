CREATE TABLE IF NOT EXISTS __ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE ai_call_logs (
        "Id" uuid NOT NULL,
        "UserId" uuid,
        "Provider" character varying(64) NOT NULL,
        "Model" character varying(128) NOT NULL,
        "Operation" character varying(32) NOT NULL,
        "InputTokens" integer NOT NULL,
        "OutputTokens" integer NOT NULL,
        "LatencyMs" integer NOT NULL,
        "Success" boolean NOT NULL,
        "ErrorCode" character varying(64),
        "ErrorMessage" character varying(2000),
        "EstimatedCostUsd" double precision NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_ai_call_logs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE languages (
        "Id" uuid NOT NULL,
        "Code" character varying(8) NOT NULL,
        "NameEnglish" character varying(80) NOT NULL,
        "NameRussian" character varying(80) NOT NULL,
        "NativeName" character varying(80),
        "FlagEmoji" character varying(16),
        "TtsVoiceHint" character varying(128),
        "IsEnabled" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        CONSTRAINT "PK_languages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE courses (
        "Id" uuid NOT NULL,
        "LanguageId" uuid NOT NULL,
        "Slug" character varying(120) NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Description" character varying(2000),
        "Level" character varying(8) NOT NULL,
        "CoverUrl" character varying(512),
        "AccentColor" character varying(16),
        "EstimatedMinutes" integer NOT NULL,
        "IsPublished" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_courses" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_courses_languages_LanguageId" FOREIGN KEY ("LanguageId") REFERENCES languages ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE grammar_topics (
        "Id" uuid NOT NULL,
        "LanguageId" uuid NOT NULL,
        "Slug" character varying(160) NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Summary" character varying(1000),
        "ExplanationMarkdown" text,
        "MinLevel" character varying(8) NOT NULL,
        "IsPublished" boolean NOT NULL,
        CONSTRAINT "PK_grammar_topics" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_grammar_topics_languages_LanguageId" FOREIGN KEY ("LanguageId") REFERENCES languages ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE lexical_units (
        "Id" uuid NOT NULL,
        "LanguageId" uuid NOT NULL,
        "TranslationLanguageId" uuid NOT NULL,
        "Text" character varying(400) NOT NULL,
        "NormalizedText" character varying(400) NOT NULL,
        "Transcription" character varying(200),
        "Translation" character varying(1000),
        "AlternativeTranslations" text[],
        "PartOfSpeech" character varying(24) NOT NULL,
        "Gender" character varying(16),
        "PluralForm" character varying(200),
        "PastTense" character varying(200),
        "AudioUrl" character varying(512),
        "ExampleTarget" character varying(1000),
        "ExampleNative" character varying(1000),
        "Notes" character varying(4000),
        "Tags" text[],
        "MinLearnerLevel" character varying(8) NOT NULL,
        "FrequencyRank" integer NOT NULL,
        "Status" character varying(24) NOT NULL,
        "ContentHash" character varying(64),
        "AuthorUserId" uuid,
        "CreatedAt" timestamptz NOT NULL,
        "UpdatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_lexical_units" PRIMARY KEY ("Id"),
        CONSTRAINT ck_lexical_units_fts_text_not_empty CHECK (length(trim("Text")) > 0),
        CONSTRAINT "FK_lexical_units_languages_LanguageId" FOREIGN KEY ("LanguageId") REFERENCES languages ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_lexical_units_languages_TranslationLanguageId" FOREIGN KEY ("TranslationLanguageId") REFERENCES languages ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE users (
        "Id" uuid NOT NULL,
        "Email" character varying(320) NOT NULL,
        "PasswordHash" character varying(512) NOT NULL,
        "DisplayName" character varying(120) NOT NULL,
        "Role" character varying(32) NOT NULL,
        "AvatarUrl" character varying(512),
        "InterfaceLanguageId" uuid NOT NULL,
        "TargetLanguageId" uuid NOT NULL,
        "Level" character varying(8) NOT NULL,
        "TargetLevel" character varying(8) NOT NULL,
        "DailyGoalXp" integer NOT NULL,
        "SpeechRate" double precision NOT NULL,
        "PreferredAiProvider" character varying(64),
        "PreferredChatModel" character varying(128),
        "WantsCorrectionHints" boolean NOT NULL,
        "PushNotificationsEnabled" boolean NOT NULL,
        "TotalXp" integer NOT NULL,
        "CurrentStreak" integer NOT NULL,
        "LongestStreak" integer NOT NULL,
        "LastActivityDate" date,
        "CreatedAt" timestamptz NOT NULL,
        "UpdatedAt" timestamptz NOT NULL,
        "LastLoginAt" timestamptz,
        "IsActive" boolean NOT NULL,
        "EmailConfirmed" boolean NOT NULL,
        "AiCallsToday" integer NOT NULL,
        "AiCallsDate" date,
        CONSTRAINT "PK_users" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_users_languages_InterfaceLanguageId" FOREIGN KEY ("InterfaceLanguageId") REFERENCES languages ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_users_languages_TargetLanguageId" FOREIGN KEY ("TargetLanguageId") REFERENCES languages ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE lessons (
        "Id" uuid NOT NULL,
        "CourseId" uuid NOT NULL,
        "Slug" character varying(160) NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Summary" character varying(1000),
        "ContentMarkdown" text,
        "SortOrder" integer NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "IsPublished" boolean NOT NULL,
        "GrammarTopicId" uuid,
        "KeyVocabulary" text[],
        CONSTRAINT "PK_lessons" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_lessons_courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES courses ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_lessons_grammar_topics_GrammarTopicId" FOREIGN KEY ("GrammarTopicId") REFERENCES grammar_topics ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE lexical_unit_embeddings (
        "Id" uuid NOT NULL,
        "LexicalUnitId" uuid NOT NULL,
        "Provider" character varying(64) NOT NULL,
        "Model" character varying(128) NOT NULL,
        "Dimensions" integer NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "UpdatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_lexical_unit_embeddings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_lexical_unit_embeddings_lexical_units_LexicalUnitId" FOREIGN KEY ("LexicalUnitId") REFERENCES lexical_units ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE chat_sessions (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Title" character varying(200),
        "Mode" character varying(32) NOT NULL,
        "Level" character varying(8) NOT NULL,
        "Scenario" character varying(2000),
        "SystemPromptOverride" character varying(8000),
        "Provider" character varying(64) NOT NULL,
        "Model" character varying(128) NOT NULL,
        "UseDictionaryContext" boolean NOT NULL,
        "IsArchived" boolean NOT NULL,
        "TotalInputTokens" integer NOT NULL,
        "TotalOutputTokens" integer NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "LastMessageAt" timestamptz NOT NULL,
        CONSTRAINT "PK_chat_sessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_chat_sessions_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE course_enrollments (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CourseId" uuid NOT NULL,
        "EnrolledAt" timestamptz NOT NULL,
        "IsCompleted" boolean NOT NULL,
        "CompletedAt" timestamptz,
        CONSTRAINT "PK_course_enrollments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_course_enrollments_courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES courses ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_course_enrollments_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE decks (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Name" character varying(160) NOT NULL,
        "Description" character varying(1000),
        "LanguageCode" character varying(8),
        "CoverEmoji" character varying(16),
        "Tags" text[],
        "IsArchived" boolean NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "UpdatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_decks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_decks_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE media_assets (
        "Id" uuid NOT NULL,
        "UserId" uuid,
        "Kind" character varying(32) NOT NULL,
        "StoragePath" character varying(600) NOT NULL,
        "ContentType" character varying(120) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "DurationMs" integer,
        "SourceText" character varying(4000),
        "Provider" character varying(64),
        "Model" character varying(128),
        "IsPublic" boolean NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "ExpiresAt" timestamptz,
        CONSTRAINT "PK_media_assets" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_media_assets_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE password_reset_tokens (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAt" timestamptz NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "UsedAt" timestamptz,
        CONSTRAINT "PK_password_reset_tokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_password_reset_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE refresh_tokens (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAt" timestamptz NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "RevokedAt" timestamptz,
        "ReplacedByTokenId" uuid,
        "RevokedByRotation" boolean NOT NULL,
        "UserAgent" character varying(400),
        "IpAddress" character varying(64),
        CONSTRAINT "PK_refresh_tokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_refresh_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE user_daily_stats (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Date" date NOT NULL,
        "XpEarned" integer NOT NULL,
        "ReviewsCompleted" integer NOT NULL,
        "CorrectAnswers" integer NOT NULL,
        "NewWordsLearned" integer NOT NULL,
        "ExercisesCompleted" integer NOT NULL,
        "ChatMessagesSent" integer NOT NULL,
        "MinutesStudied" integer NOT NULL,
        "GoalReached" boolean NOT NULL,
        CONSTRAINT "PK_user_daily_stats" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_user_daily_stats_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE exercises (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid,
        "CourseId" uuid,
        "LessonId" uuid,
        "LanguageId" uuid NOT NULL,
        "TranslationLanguageId" uuid,
        "Type" character varying(32) NOT NULL,
        "Title" character varying(300) NOT NULL,
        "Instructions" character varying(2000),
        "Prompt" character varying(2000),
        "Payload" jsonb,
        "ExplanationMarkdown" text,
        "Level" character varying(8) NOT NULL,
        "Topics" text[],
        "TargetLexicalUnitIds" uuid[],
        "Points" integer NOT NULL,
        "EstimatedSeconds" integer NOT NULL,
        "Source" character varying(24) NOT NULL,
        "AiProvider" character varying(64),
        "AiModel" character varying(128),
        "AiConfidence" double precision,
        "IsPublished" boolean NOT NULL,
        "IsActive" boolean NOT NULL,
        "UsageCount" integer NOT NULL,
        "CorrectRateBasisPoints" integer NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        "LastUsedAt" timestamptz,
        CONSTRAINT "PK_exercises" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_exercises_courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES courses ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_exercises_languages_LanguageId" FOREIGN KEY ("LanguageId") REFERENCES languages ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_exercises_lessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES lessons ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_exercises_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE chat_messages (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "Role" integer NOT NULL,
        "Content" text NOT NULL,
        "AudioUrl" character varying(512),
        "Provider" character varying(64),
        "Model" character varying(128),
        "InputTokens" integer NOT NULL,
        "OutputTokens" integer NOT NULL,
        "LatencyMs" integer NOT NULL,
        "RagContextRefs" uuid[],
        "SourceLanguageCode" character varying(8),
        "FeedbackRating" integer,
        "CreatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_chat_messages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_chat_messages_chat_sessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES chat_sessions ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE lesson_progress (
        "Id" uuid NOT NULL,
        "EnrollmentId" uuid NOT NULL,
        "LessonId" uuid NOT NULL,
        "Status" character varying(24) NOT NULL,
        "ProgressPercent" integer NOT NULL,
        "BestScorePercent" integer NOT NULL,
        "Attempts" integer NOT NULL,
        "StartedAt" timestamptz,
        "CompletedAt" timestamptz,
        CONSTRAINT "PK_lesson_progress" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_lesson_progress_course_enrollments_EnrollmentId" FOREIGN KEY ("EnrollmentId") REFERENCES course_enrollments ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_lesson_progress_lessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES lessons ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE user_lexical_units (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "LexicalUnitId" uuid NOT NULL,
        "Source" character varying(24) NOT NULL,
        "PersonalNote" character varying(2000),
        "State" character varying(24) NOT NULL,
        "Suspended" boolean NOT NULL,
        "CorrectStreak" integer NOT NULL,
        "IncorrectStreak" integer NOT NULL,
        "MasteryScore" integer NOT NULL,
        "AddedAt" timestamptz NOT NULL,
        "LastReviewedAt" timestamptz,
        "LastDeckId" uuid,
        CONSTRAINT "PK_user_lexical_units" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_user_lexical_units_decks_LastDeckId" FOREIGN KEY ("LastDeckId") REFERENCES decks ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_user_lexical_units_lexical_units_LexicalUnitId" FOREIGN KEY ("LexicalUnitId") REFERENCES lexical_units ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_user_lexical_units_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE pronunciation_attempts (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "MediaAssetId" uuid,
        "LexicalUnitId" uuid,
        "TargetText" text NOT NULL,
        "Transcript" character varying(4000),
        "RecognizedText" character varying(4000),
        "OverallScore" integer NOT NULL,
        "AccuracyScore" integer NOT NULL,
        "FluencyScore" integer NOT NULL,
        "CompletenessScore" integer NOT NULL,
        "ProsodyScore" integer NOT NULL,
        "WordLevelScores" jsonb,
        "FeedbackMarkdown" text,
        "Provider" character varying(64),
        "Model" character varying(128),
        "XpEarned" integer NOT NULL,
        "CreatedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_pronunciation_attempts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_pronunciation_attempts_lexical_units_LexicalUnitId" FOREIGN KEY ("LexicalUnitId") REFERENCES lexical_units ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_pronunciation_attempts_media_assets_MediaAssetId" FOREIGN KEY ("MediaAssetId") REFERENCES media_assets ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_pronunciation_attempts_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE exercise_attempts (
        "Id" uuid NOT NULL,
        "ExerciseId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Answers" jsonb,
        "CorrectCount" integer NOT NULL,
        "TotalCount" integer NOT NULL,
        "ScorePercent" integer NOT NULL,
        "IsPassed" boolean NOT NULL,
        "XpEarned" integer NOT NULL,
        "AiFeedbackMarkdown" text,
        "AiProvider" character varying(64),
        "AiModel" character varying(128),
        "DurationMs" integer NOT NULL,
        "StartedAt" timestamptz NOT NULL,
        "CompletedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_exercise_attempts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_exercise_attempts_exercises_ExerciseId" FOREIGN KEY ("ExerciseId") REFERENCES exercises ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_exercise_attempts_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE deck_cards (
        "Id" uuid NOT NULL,
        "DeckId" uuid NOT NULL,
        "UserLexicalUnitId" uuid NOT NULL,
        "Position" integer NOT NULL,
        "IsNew" boolean NOT NULL,
        "AddedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_deck_cards" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_deck_cards_decks_DeckId" FOREIGN KEY ("DeckId") REFERENCES decks ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_deck_cards_user_lexical_units_UserLexicalUnitId" FOREIGN KEY ("UserLexicalUnitId") REFERENCES user_lexical_units ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE review_cards (
        "Id" uuid NOT NULL,
        "UserLexicalUnitId" uuid NOT NULL,
        "State" character varying(24) NOT NULL,
        "DueAt" timestamptz NOT NULL,
        "IntervalDays" double precision NOT NULL,
        "EaseFactor" double precision NOT NULL,
        "Repetitions" integer NOT NULL,
        "Lapses" integer NOT NULL,
        "LearningStep" integer NOT NULL,
        "MaxIntervalDays" integer NOT NULL,
        "LastReviewedAt" timestamptz,
        "SuspendedAt" timestamptz,
        CONSTRAINT "PK_review_cards" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_review_cards_user_lexical_units_UserLexicalUnitId" FOREIGN KEY ("UserLexicalUnitId") REFERENCES user_lexical_units ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE TABLE review_logs (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "ReviewCardId" uuid NOT NULL,
        "LexicalUnitId" uuid NOT NULL,
        "Rating" character varying(16) NOT NULL,
        "PreviousIntervalDays" double precision NOT NULL,
        "NewIntervalDays" double precision NOT NULL,
        "PreviousState" character varying(24) NOT NULL,
        "NewState" character varying(24) NOT NULL,
        "DurationMs" integer NOT NULL,
        "WasCorrect" boolean NOT NULL,
        "GivenAnswer" character varying(1000),
        "ReviewedAt" timestamptz NOT NULL,
        CONSTRAINT "PK_review_logs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_review_logs_lexical_units_LexicalUnitId" FOREIGN KEY ("LexicalUnitId") REFERENCES lexical_units ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_review_logs_review_cards_ReviewCardId" FOREIGN KEY ("ReviewCardId") REFERENCES review_cards ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_ai_call_logs_Operation" ON ai_call_logs ("Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_ai_call_logs_UserId_CreatedAt" ON ai_call_logs ("UserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_chat_messages_SessionId_CreatedAt" ON chat_messages ("SessionId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_chat_sessions_UserId_LastMessageAt" ON chat_sessions ("UserId", "LastMessageAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_course_enrollments_CourseId" ON course_enrollments ("CourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_course_enrollments_UserId_CourseId" ON course_enrollments ("UserId", "CourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_courses_LanguageId_Slug" ON courses ("LanguageId", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_deck_cards_DeckId_Position" ON deck_cards ("DeckId", "Position");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_deck_cards_DeckId_UserLexicalUnitId" ON deck_cards ("DeckId", "UserLexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_deck_cards_UserLexicalUnitId" ON deck_cards ("UserLexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_decks_UserId_Name" ON decks ("UserId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_exercise_attempts_ExerciseId_UserId" ON exercise_attempts ("ExerciseId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_exercise_attempts_UserId_CompletedAt" ON exercise_attempts ("UserId", "CompletedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_exercises_CourseId" ON exercises ("CourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_exercises_LanguageId_Type_IsActive" ON exercises ("LanguageId", "Type", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_exercises_LessonId" ON exercises ("LessonId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_exercises_OwnerUserId_CreatedAt" ON exercises ("OwnerUserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_grammar_topics_LanguageId_Slug" ON grammar_topics ("LanguageId", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_languages_Code" ON languages ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_lesson_progress_EnrollmentId_LessonId" ON lesson_progress ("EnrollmentId", "LessonId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_lesson_progress_LessonId" ON lesson_progress ("LessonId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_lessons_CourseId_Slug" ON lessons ("CourseId", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_lessons_GrammarTopicId" ON lessons ("GrammarTopicId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_lexical_unit_embeddings_LexicalUnitId_Provider_Model" ON lexical_unit_embeddings ("LexicalUnitId", "Provider", "Model");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_lexical_units_ContentHash" ON lexical_units ("ContentHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_lexical_units_LanguageId_NormalizedText_TranslationLanguage~" ON lexical_units ("LanguageId", "NormalizedText", "TranslationLanguageId") WHERE "Status" <> 'Deprecated';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_lexical_units_LanguageId_Status" ON lexical_units ("LanguageId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_lexical_units_NormalizedText" ON lexical_units ("NormalizedText");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_lexical_units_TranslationLanguageId" ON lexical_units ("TranslationLanguageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_media_assets_UserId_CreatedAt" ON media_assets ("UserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_password_reset_tokens_TokenHash" ON password_reset_tokens ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_password_reset_tokens_UserId" ON password_reset_tokens ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_pronunciation_attempts_LexicalUnitId" ON pronunciation_attempts ("LexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_pronunciation_attempts_MediaAssetId" ON pronunciation_attempts ("MediaAssetId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_pronunciation_attempts_UserId_CreatedAt" ON pronunciation_attempts ("UserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_refresh_tokens_TokenHash" ON refresh_tokens ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_refresh_tokens_UserId_RevokedAt" ON refresh_tokens ("UserId", "RevokedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_review_cards_State_DueAt" ON review_cards ("State", "DueAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_review_cards_UserLexicalUnitId" ON review_cards ("UserLexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_review_logs_LexicalUnitId" ON review_logs ("LexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_review_logs_ReviewCardId" ON review_logs ("ReviewCardId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_review_logs_UserId_ReviewedAt" ON review_logs ("UserId", "ReviewedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_user_daily_stats_UserId_Date" ON user_daily_stats ("UserId", "Date");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_user_lexical_units_LastDeckId" ON user_lexical_units ("LastDeckId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_user_lexical_units_LexicalUnitId" ON user_lexical_units ("LexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_user_lexical_units_UserId_LexicalUnitId" ON user_lexical_units ("UserId", "LexicalUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_users_Email" ON users ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_users_InterfaceLanguageId" ON users ("InterfaceLanguageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    CREATE INDEX "IX_users_TargetLanguageId" ON users ("TargetLanguageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260927130152_InitialCreate') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260927130152_InitialCreate', '10.0.12');
    END IF;
END $EF$;
COMMIT;

