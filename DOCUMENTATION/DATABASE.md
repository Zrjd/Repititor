# База данных Repetitor

Полное описание схемы PostgreSQL: таблицы, колонки, связи, индексы и бизнес-назначение.

- **СУБД:** PostgreSQL 17 + расширения `pgvector` (векторный поиск) и `pg_trgm` (нечёткий поиск)
- **Миграции:** EF Core (`__ef_migrations_history`) + ручные SQL-патчи (`schema_patches`)
- **Идентификаторы:** везде `uuid` (gen_random_uuid)
- **Время:** `timestamp with time zone` (timestamptz)

---

## Обзор предметной области

```
┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│  languages  │◄────┤   courses    │◄────┤   lessons   │
└──────┬──────┘     └──────┬───────┘     └──────┬──────┘
       │                   │                    │
       │            ┌──────┴───────┐     ┌──────┴──────────┐
       │            │   exercises  │     │ grammar_topics  │
       │            └──────┬───────┘     └─────────────────┘
       │                   │
┌──────┴──────┐     ┌──────┴───────────┐
│lexical_units│◄────┤exercise_attempts │
└──────┬──────┘     └──────────────────┘
       │
       ├──────────────┬──────────────────┐
       │              │                  │
┌──────┴───────┐ ┌───┴──────────┐ ┌─────┴──────────┐
│user_lexical_ │ │ review_cards │ │lexical_unit_   │
│   units      │ └──────┬───────┘ │  embeddings    │
└──────┬───────┘        │         └────────────────┘
       │         ┌──────┴───────┐
┌──────┴──────┐  │ review_logs  │
│  deck_cards │  └──────────────┘
└─────────────┘
```

Подсистема учебных групп (учитель набирает учеников и выдаёт им курсы):

```
┌──────────────┐   ┌───────────────────┐   ┌───────────────────────┐
│    users     │◄──┤   study_groups    │◄──┤  study_group_members  │
│  (учитель,   │   │ владелец-учитель │   │  ученики группы      │
│   ученики)   │   └─────────┬─────────┘   └───────────────────────┘
└──────────────┘             │
                  ┌──────────┴──────────┐
                  │ study_group_courses  │      назначение курса →
                  └──────────┬──────────┘
                             ▼
                  ┌──────────────────────┐
                  │ course_enrollments   │  ← автоматическая выдача
                  └──────────────────────┘     курса участникам

                  ┌──────────────────────┐
                  │ study_group_         │  код-приглашение, срок
                  │   invitations        │  действия, лимит применений
                  └──────────────────────┘
```

---

## 1. Идентификация и аутентификация

### `users`
Пользователи системы (ученики, учителя, админы).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `Email` | varchar(320) UNIQUE | Логин |
| `PasswordHash` | varchar(512) | Хеш пароля (Argon2/bcrypt) |
| `DisplayName` | varchar(120) | Отображаемое имя |
| `Role` | varchar(32) | `Learner` / `Teacher` / `Admin` |
| `AvatarUrl` | varchar(512) | Аватар |
| `InterfaceLanguageId` | uuid FK → languages | Язык интерфейса |
| `TargetLanguageId` | uuid FK → languages | Изучаемый язык |
| `Level` | varchar(8) | Текущий уровень (A1–C2) |
| `TargetLevel` | varchar(8) | Целевой уровень |
| `DailyGoalXp` | integer | Дневная цель в XP |
| `SpeechRate` | double | Скорость озвучки (0.5–2.0) |
| `PreferredAiProvider` | varchar(64) | `ollama` / `openai` |
| `PreferredChatModel` | varchar(128) | |
| `WantsCorrectionHints` | boolean | |
| `PushNotificationsEnabled` | boolean | |
| `TotalXp` | integer | Накопленный XP |
| `CurrentStreak` | integer | Серия дней подряд |
| `LongestStreak` | integer | Максимальная серия |
| `LastActivityDate` | date | |
| `CreatedAt` / `UpdatedAt` | timestamptz | |
| `LastLoginAt` | timestamptz | |
| `IsActive` | boolean | Мягкая блокировка |
| `EmailConfirmed` | boolean | |
| `AiCallsToday` | integer | Счётчик AI-вызовов за день |
| `AiCallsDate` | date | Дата счётчика |

### `refresh_tokens`
Ротация refresh-токенов (JWT). При обновлении старый токен отзывается.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `TokenHash` | varchar(128) UNIQUE | Хеш токена |
| `ExpiresAt` | timestamptz | |
| `CreatedAt` | timestamptz | |
| `RevokedAt` | timestamptz NULL | |
| `ReplacedByTokenId` | uuid NULL | Кем заменён |
| `RevokedByRotation` | boolean | |
| `UserAgent` / `IpAddress` | | Аудит |

### `password_reset_tokens`
Токены сброса пароля (одноразовые).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `TokenHash` | varchar(128) UNIQUE | |
| `ExpiresAt` / `CreatedAt` / `UsedAt` | timestamptz | |

---

## 2. Лексикон (словарь)

### `languages`
Справочник языков.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `Code` | varchar(8) UNIQUE | `en`, `ru`, `de`… |
| `NameEnglish` / `NameRussian` / `NativeName` | varchar(80) | Названия |
| `FlagEmoji` | varchar(16) | |
| `TtsVoiceHint` | varchar(128) | Подсказка голоса TTS |
| `IsEnabled` | boolean | |
| `SortOrder` | integer | |

### `lexical_units`
Слова и выражения — ядро словаря.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `LanguageId` | uuid FK → languages RESTRICT | Язык слова |
| `TranslationLanguageId` | uuid FK → languages RESTRICT | Язык перевода |
| `Text` | varchar(400) | Слово/фраза (CHECK: не пустой) |
| `NormalizedText` | varchar(400) | Нормализованная форма |
| `Transcription` | varchar(200) | Транскрипция |
| `Translation` | varchar(1000) | Перевод |
| `AlternativeTranslations` | text[] | |
| `PartOfSpeech` | varchar(24) | `Noun`, `Verb`, `Adjective`… |
| `Gender` | varchar(16) | Род |
| `PluralForm` / `PastTense` | varchar(200) | Словоформы |
| `AudioUrl` | varchar(512) | Озвучка |
| `ExampleTarget` / `ExampleNative` | varchar(1000) | Пример предложения |
| `Notes` | varchar(4000) | |
| `Tags` | text[] | |
| `MinLearnerLevel` | varchar(8) | Минимальный уровень |
| `FrequencyRank` | integer | Частотность |
| `Status` | varchar(24) | `Draft` / `Verified` / `Deprecated` |
| `ContentHash` | varchar(64) | Для дедупликации |
| `AuthorUserId` | uuid NULL | Кто добавил |
| `CreatedAt` / `UpdatedAt` | timestamptz | |

**Индексы:** уникальный `(LanguageId, NormalizedText, TranslationLanguageId)` WHERE Status ≠ Deprecated; GIN-индексы `pg_trgm` на `Text` и `Translation` (нечёткий поиск).

### `lexical_unit_embeddings`
Векторные представления слов для семантического поиска (RAG).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `LexicalUnitId` | uuid FK → lexical_units CASCADE | |
| `Provider` / `Model` | varchar | Модель эмбеддинга |
| `Dimensions` | integer | 1536 / 1024 |
| `ContentHash` | varchar(64) | Хеш контента |
| `embedding` | vector(1536) | Основной вектор |
| `embedding_local` | vector(1024) | Локальная модель |
| `CreatedAt` / `UpdatedAt` | timestamptz | |

**Индексы:** HNSW (cosine) на обоих векторах; уникальный `(LexicalUnitId, Provider, Model)`.

### `user_lexical_units`
Слова пользователя («Мои слова») — связь пользователя со словарём.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `LexicalUnitId` | uuid FK → lexical_units CASCADE | |
| `Source` | varchar(24) | `Curated` / `AiGenerated` / `UserCreated` / `Imported` |
| `PersonalNote` | varchar(2000) | |
| `State` | varchar(24) | `New` / `Learning` / `Review` / `Relearning` / `Mastered` |
| `Suspended` | boolean | |
| `CorrectStreak` / `IncorrectStreak` | integer | |
| `MasteryScore` | integer | 0–100 |
| `AddedAt` / `LastReviewedAt` | timestamptz | |
| `LastDeckId` | uuid FK → decks SET NULL | |

**Уникальный индекс:** `(UserId, LexicalUnitId)`.

---

## 3. Каталог контента

### `courses`
Курсы по языкам и уровням.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `LanguageId` | uuid FK → languages CASCADE | |
| `Slug` | varchar(120) | URL-идентификатор |
| `Title` / `Description` | varchar | |
| `Level` | varchar(8) | A1–C2 |
| `CoverUrl` / `AccentColor` | | Оформление |
| `EstimatedMinutes` | integer | |
| `IsPublished` | boolean | |
| `SortOrder` | integer | |
| `OwnerUserId` | uuid FK → users SET NULL NULL | Владелец-учитель (NULL = системный курс) |
| `CreatedAt` | timestamptz | |

**Индексы:** уникальный `(LanguageId, Slug)`; индекс по `OwnerUserId`.
Курс с `OwnerUserId = NULL` — системный и доступен всем, в том числе для назначения группам.

### `lessons`
Уроки внутри курсов.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `CourseId` | uuid FK → courses CASCADE | |
| `Slug` | varchar(160) | |
| `Title` / `Summary` | varchar | |
| `ContentMarkdown` | text | Материал урока |
| `SortOrder` | integer | |
| `EstimatedMinutes` | integer | |
| `IsPublished` | boolean | |
| `GrammarTopicId` | uuid FK → grammar_topics SET NULL | |
| `KeyVocabulary` | text[] | Ключевые слова |

**Уникальный индекс:** `(CourseId, Slug)`.

### `grammar_topics`
Грамматические темы.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `LanguageId` | uuid FK → languages CASCADE | |
| `Slug` | varchar(160) | |
| `Title` / `Summary` | varchar | |
| `ExplanationMarkdown` | text | |
| `MinLevel` | varchar(8) | |
| `IsPublished` | boolean | |

**Уникальный индекс:** `(LanguageId, Slug)`.

---

## 4. Обучение

### `course_enrollments`
Запись пользователя на курс.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `CourseId` | uuid FK → courses CASCADE | |
| `EnrolledAt` | timestamptz | |
| `IsCompleted` | boolean | |
| `CompletedAt` | timestamptz NULL | |

**Уникальный индекс:** `(UserId, CourseId)`.

### `lesson_progress`
Прогресс по урокам.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `EnrollmentId` | uuid FK → course_enrollments CASCADE | |
| `LessonId` | uuid FK → lessons CASCADE | |
| `Status` | varchar(24) | `NotStarted` / `InProgress` / `Completed` |
| `ProgressPercent` | integer | 0–100 |
| `BestScorePercent` | integer | |
| `Attempts` | integer | |
| `StartedAt` / `CompletedAt` | timestamptz | |

**Уникальный индекс:** `(EnrollmentId, LessonId)`.

### `study_groups`
Учебная группа. Группа из одного участника используется как персональные занятия —
отдельного типа группы в схеме нет.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `TeacherUserId` | uuid FK → users CASCADE | Преподаватель-владелец |
| `Name` | varchar(160) | Название группы / имя ученика для персональных занятий |
| `Description` | varchar(1000) NULL | |
| `CreatedAt` | timestamptz | |

**Индексы:** `(TeacherUserId, Name)`; FK на `users` CASCADE — при удалении учителя его группы удаляются.
Персональность группы не хранится: определяется по числу участников (`study_group_members`).

### `study_group_members`
Состав группы. Ученик добавляется учителем по email или самостоятельно по коду-приглашению.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `GroupId` | uuid FK → study_groups CASCADE | |
| `UserId` | uuid FK → users CASCADE | Участник |
| `JoinedAt` | timestamptz | |

**Индексы:** уникальный `(GroupId, UserId)` — повторное добавление не дублирует;
индекс по `UserId` для выборки групп пользователя.

### `study_group_courses`
Курсы, назначенные группе. Назначение курса создаёт `course_enrollments`
для всех текущих участников, а новые участники получают курс при добавлении в группу.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `GroupId` | uuid FK → study_groups CASCADE | |
| `CourseId` | uuid FK → courses CASCADE | |
| `AssignedAt` | timestamptz | |

**Индексы:** уникальный `(GroupId, CourseId)`; индекс по `CourseId`.

### `study_group_invitations`
Коды-приглашения для вступления в группу.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `GroupId` | uuid FK → study_groups CASCADE | |
| `Code` | varchar(16) UNIQUE | Код в верхнем регистре (8 символов) |
| `ExpiresAt` | timestamptz | Срок действия |
| `MaxUses` | integer | 0 = без ограничений |
| `UsedCount` | integer | Сколько раз код применён |
| `IsRevoked` | boolean | Отозван учителем |
| `CreatedAt` | timestamptz | |

**Индексы:** уникальный `Code`; `(GroupId, IsRevoked)`.
Код считается действующим, если не отозван, не истёк и `UsedCount < MaxUses` (при `MaxUses > 0`).

### `exercises`
Упражнения (генерируются AI или создаются учителем).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `OwnerUserId` | uuid FK → users CASCADE NULL | Автор (NULL = системное) |
| `CourseId` | uuid FK → courses SET NULL | |
| `LessonId` | uuid FK → lessons SET NULL | |
| `LanguageId` | uuid FK → languages RESTRICT | |
| `TranslationLanguageId` | uuid FK → languages | |
| `Type` | varchar(32) | `MultipleChoice`, `TranslateToTarget`, `GapFill`, `Listening`, `Speaking`… |
| `Title` / `Instructions` / `Prompt` | varchar | |
| `Payload` | jsonb | Варианты ответов, пропуски |
| `ExplanationMarkdown` | text | |
| `Level` | varchar(8) | |
| `Topics` | text[] | |
| `TargetLexicalUnitIds` | uuid[] | |
| `Points` | integer | |
| `EstimatedSeconds` | integer | |
| `Source` | varchar(24) | `Curated` / `AiGenerated` / `UserCreated` / `Imported` |
| `AiProvider` / `AiModel` / `AiConfidence` | | Метаданные генерации |
| `IsPublished` / `IsActive` | boolean | |
| `UsageCount` | integer | |
| `CorrectRateBasisPoints` | integer | 0–10000 |
| `CreatedAt` / `LastUsedAt` | timestamptz | |

### `exercise_attempts`
Результаты выполнения упражнений.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `ExerciseId` | uuid FK → exercises CASCADE | |
| `UserId` | uuid FK → users CASCADE | |
| `Answers` | jsonb | Ответы пользователя |
| `CorrectCount` / `TotalCount` | integer | |
| `ScorePercent` | integer | |
| `IsPassed` | boolean | |
| `XpEarned` | integer | |
| `AiFeedbackMarkdown` | text | Разбор от AI |
| `AiProvider` / `AiModel` | | |
| `DurationMs` | integer | |
| `StartedAt` / `CompletedAt` | timestamptz | |

---

## 5. Практика (SRS)

### `decks`
Колоды карточек пользователя.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `Name` | varchar(160) | |
| `Description` | varchar(1000) | |
| `LanguageCode` | varchar(8) | |
| `CoverEmoji` | varchar(16) | |
| `Tags` | text[] | |
| `IsArchived` | boolean | |
| `CreatedAt` / `UpdatedAt` | timestamptz | |

### `deck_cards`
Состав колоды.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `DeckId` | uuid FK → decks CASCADE | |
| `UserLexicalUnitId` | uuid FK → user_lexical_units CASCADE | |
| `Position` | integer | Порядок |
| `IsNew` | boolean | |
| `AddedAt` | timestamptz | |

**Уникальный индекс:** `(DeckId, UserLexicalUnitId)`.

### `review_cards`
SRS-состояние карточки (расписание повторений).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserLexicalUnitId` | uuid FK → user_lexical_units CASCADE | |
| `State` | varchar(24) | `New` / `Learning` / `Review` / `Relearning` / `Mastered` |
| `DueAt` | timestamptz | Когда пора повторить |
| `IntervalDays` | double | Текущий интервал |
| `EaseFactor` | double | Коэффициент лёкости (SM-2) |
| `Repetitions` / `Lapses` | integer | |
| `LearningStep` | integer | Шаг обучения |
| `MaxIntervalDays` | integer | |
| `LastReviewedAt` | timestamptz | |
| `SuspendedAt` | timestamptz NULL | |

**Уникальный индекс:** `(UserLexicalUnitId)`; индекс `(State, DueAt)` для выборки due-карточек.

### `review_logs`
Журнал повторений (история оценок).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `ReviewCardId` | uuid FK → review_cards CASCADE | |
| `LexicalUnitId` | uuid FK → lexical_units CASCADE | |
| `Rating` | varchar(16) | `Again` / `Hard` / `Good` / `Easy` |
| `PreviousIntervalDays` / `NewIntervalDays` | double | |
| `PreviousState` / `NewState` | varchar(24) | |
| `DurationMs` | integer | |
| `WasCorrect` | boolean | |
| `GivenAnswer` | varchar(1000) | |
| `ReviewedAt` | timestamptz | |

---

## 6. AI-репетитор

### `chat_sessions`
Диалоги с AI-репетитором.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `Title` | varchar(200) | |
| `Mode` | varchar(32) | `FreePractice` / `LessonRoleplay` / `ExamPreparation` / `GrammarHelp` / `Interview` / `Travel` |
| `Level` | varchar(8) | |
| `Scenario` | varchar(2000) | |
| `SystemPromptOverride` | varchar(8000) | |
| `Provider` / `Model` | varchar | |
| `UseDictionaryContext` | boolean | Использовать словарь в RAG |
| `IsArchived` | boolean | |
| `TotalInputTokens` / `TotalOutputTokens` | integer | |
| `CreatedAt` / `LastMessageAt` | timestamptz | |

### `chat_messages`
Сообщения диалога.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `SessionId` | uuid FK → chat_sessions CASCADE | |
| `Role` | integer | `System`=0 / `User`=1 / `Assistant`=2 |
| `Content` | text | |
| `AudioUrl` | varchar(512) | Озвучка ответа |
| `Provider` / `Model` | | |
| `InputTokens` / `OutputTokens` / `LatencyMs` | integer | |
| `RagContextRefs` | uuid[] | Ссылки на контекст RAG |
| `SourceLanguageCode` | varchar(8) | |
| `FeedbackRating` | integer NULL | Оценка сообщения |
| `CreatedAt` | timestamptz | |

### `ai_call_logs`
Аудит AI-вызовов (токены, стоимость, ошибки).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `Provider` / `Model` | varchar | |
| `Operation` | varchar(32) | `ChatCompletion` / `Embedding` / `TextToSpeech` / `SpeechToText` / `ExerciseGeneration` / `AnswerGrading` / `PronunciationAssessment` |
| `InputTokens` / `OutputTokens` / `LatencyMs` | integer | |
| `Success` | boolean | |
| `ErrorCode` / `ErrorMessage` | | |
| `EstimatedCostUsd` | double | |
| `CreatedAt` | timestamptz | |

---

## 7. Произношение

### `media_assets`
Аудиофайлы (TTS, записи пользователя).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `Kind` | varchar(32) | `GeneratedSpeech` / `UserRecording` / `LessonAudio` / `PronunciationSample` |
| `StoragePath` | varchar(600) | |
| `ContentType` | varchar(120) | |
| `SizeBytes` | bigint | |
| `DurationMs` | integer | |
| `SourceText` | varchar(4000) | |
| `Provider` / `Model` | | |
| `IsPublic` | boolean | |
| `CreatedAt` / `ExpiresAt` | timestamptz | |

### `pronunciation_attempts`
Результаты распознавания речи.

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `MediaAssetId` | uuid FK → media_assets SET NULL | |
| `LexicalUnitId` | uuid FK → lexical_units SET NULL | |
| `TargetText` | text | Что нужно было сказать |
| `Transcript` | varchar(4000) | Что распознано |
| `RecognizedText` | varchar(4000) | |
| `OverallScore` / `AccuracyScore` / `FluencyScore` / `CompletenessScore` / `ProsodyScore` | integer | 0–100 |
| `WordLevelScores` | jsonb | Попсловная оценка |
| `FeedbackMarkdown` | text | |
| `Provider` / `Model` | | |
| `XpEarned` | integer | |
| `CreatedAt` | timestamptz | |

---

## 8. Статистика

### `user_daily_stats`
Агрегаты по дням (для дашборда и серий).

| Колонка | Тип | Описание |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → users CASCADE | |
| `Date` | date | |
| `XpEarned` | integer | |
| `ReviewsCompleted` | integer | |
| `CorrectAnswers` | integer | |
| `NewWordsLearned` | integer | |
| `ExercisesCompleted` | integer | |
| `ChatMessagesSent` | integer | |
| `MinutesStudied` | integer | |
| `GoalReached` | boolean | |

**Уникальный индекс:** `(UserId, Date)`.

---

## 9. Системные таблицы

### `__ef_migrations_history`
Журнал миграций EF Core (`MigrationId`, `ProductVersion`).

### `schema_patches`
Ручные SQL-патчи (`id`, `applied_at`).

### `app_settings`
Глобальные настройки приложения в виде JSON-значений (AI-провайдеры и модели, промпты, лимиты).

| Колонка | Тип | Описание |
|---|---|---|
| `Key` | varchar(120) PK | |
| `Value` | jsonb | Значение |
| `UpdatedAt` | timestamptz | |

### `vw_user_learning_summary`
Представление: сводка по пользователю (XP, серии, слова, due-карточки, попытки за 7 дней).

---

## 10. Перечисления (enums)

| Enum | Значения |
|---|---|
| `UserRole` | Learner, Teacher, Admin |
| `CefrLevel` | A1, A2, B1, B2, C1, C2 |
| `CardState` | New, Learning, Review, Relearning, Mastered |
| `ReviewRating` | Again, Hard, Good, Easy |
| `ContentStatus` | Draft, Verified, Deprecated |
| `PartOfSpeech` | Unknown, Noun, Verb, Adjective, Adverb, Pronoun, Preposition, Conjunction, Interjection, Numeral, Article, Phrase, Expression |
| `ExerciseType` | MultipleChoice, TranslateToTarget, TranslateFromTarget, GapFill, WordOrder, MatchPairs, Listening, Writing, Speaking, FillInTheBlanks |
| `ContentSource` | Curated, AiGenerated, UserCreated, Imported |
| `ChatRole` | System, User, Assistant |
| `TutorMode` | FreePractice, LessonRoleplay, ExamPreparation, GrammarHelp, Interview, Travel |
| `MediaKind` | GeneratedSpeech, UserRecording, LessonAudio, PronunciationSample |
| `AiOperation` | ChatCompletion, Embedding, TextToSpeech, SpeechToText, ExerciseGeneration, AnswerGrading, PronunciationAssessment |
| `LessonProgressStatus` | NotStarted, InProgress, Completed |

---

## 11. Правила целостности

- **Каскадное удаление:** при удалении пользователя удаляются его токены, колоды, карточки, слова, сессии, статистика.
- **Каскадное удаление:** при удалении группы удаляются её участники, назначенные курсы и коды-приглашения; при удалении учителя удаляются все его группы.
- **Каскадное удаление:** при удалении курса удаляются его назначения группам.
- **SET NULL:** при удалении учителя, создавшего курс, курс становится системным (`OwnerUserId = NULL`), а не удаляется; при удалении учителя его записи на курсы и прогресс сохраняются.
- **SET NULL:** при удалении курса/урока упражнения и прогресс не удаляются, а теряют привязку.
- **RESTRICT:** нельзя удалить язык, на который ссылаются пользователи или словари.
- **Уникальность:** email пользователя, код языка, slug курса/урока/темы в рамках языка, слово в рамках языка, слово пользователя, карточка пользователя, дневная статистика, код-приглашение группы, пара «группа — участник», пара «группа — курс».
- **Бизнес-правила:** запись на курс, выданная через группу, не отзывается при исключении ученика из группы, снятии курса с группы или удалении группы — прогресс ученика сохраняется.

---

## 12. Расширения PostgreSQL

| Расширение | Назначение |
|---|---|
| `vector` | Тип `vector(n)`, HNSW-индексы для семантического поиска |
| `pg_trgm` | GIN-индексы для нечёткого поиска по словам и переводам |
