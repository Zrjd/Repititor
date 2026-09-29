using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Универсальная страница результатов, полученная из базы данных.
/// Нужна, чтобы сервисы не зависели от DTO-слоя API: контроллер сам превращает её в <c>PagedResponse</c>.
/// </summary>
public sealed record DbPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>
/// Описание актора, выполняющего операцию. Используется сервисами, где проверка прав
/// тесно связана с записью в базу и должна выполняться до сохранения изменений.
/// </summary>
public sealed record DbActor(Guid UserId, UserRole Role);

/// <summary>Результат частичного обновления профиля пользователя.</summary>
public sealed record UserProfileUpdateResult(User? User, string? InvalidLanguageField)
{
    /// <summary>Признак того, что указанный в запросе язык не найден в справочнике.</summary>
    public bool HasInvalidLanguage => InvalidLanguageField is not null;
}

/// <summary>Ежедневная статистика пользователя за один день.</summary>
public sealed record DailyActivityItem(
    DateOnly Date,
    int XpEarned,
    int ReviewsCompleted,
    int CorrectAnswers,
    int NewWordsLearned,
    int ExercisesCompleted,
    int MinutesStudied);

/// <summary>Слово, рекомендованное к повторению на дашборде.</summary>
public sealed record DueWordItem(
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    string? AudioUrl,
    string State,
    int DueInSeconds);

/// <summary>
/// Всё необходимое для главного экрана пользователя. Собирается одним вызовом,
/// чтобы контроллеру не пришлось делать семь отдельных запросов к базе.
/// </summary>
public sealed record UserDashboardSnapshot(
    int CardsDue,
    int NewCards,
    int TotalWords,
    int MasteredWords,
    int ExercisesDone7d,
    int ReviewsToday,
    int XpToday,
    int[] Streak,
    IReadOnlyList<DueWordItem> RecommendedWords,
    IReadOnlyList<DailyActivityItem> Activity);

/// <summary>Курс вместе с прогрессом конкретного пользователя для публичного каталога.</summary>
public sealed record CourseCatalogItem(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    string Level,
    Guid LanguageId,
    string LanguageCode,
    string? CoverUrl,
    string? AccentColor,
    int EstimatedMinutes,
    int PublishedLessonsCount,
    int ProgressPercent,
    bool IsEnrolled);

/// <summary>Урок каталога вместе со статусом прохождения у пользователя.</summary>
public sealed record LessonCatalogItem(
    Guid Id,
    Guid CourseId,
    string Slug,
    string Title,
    string? Summary,
    int SortOrder,
    int EstimatedMinutes,
    string[]? KeyVocabulary,
    string? ContentMarkdown,
    string Status,
    int ProgressPercent,
    int BestScorePercent);

/// <summary>Тема грамматики в упрощённом виде для публичного каталога.</summary>
public sealed record GrammarTopicItem(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string MinLevel,
    string? ExplanationMarkdown);

/// <summary>Результат загрузки уроков курса: признак существования курса и сами уроки.</summary>
public sealed record CourseLessonsResult(bool CourseExists, IReadOnlyList<LessonCatalogItem> Lessons);

/// <summary>
/// Итог операции над объектом каталога с учётом прав: учитель управляет только своими курсами и уроками,
/// администратор — всеми.
/// </summary>
public enum CatalogMutationStatus
{
    /// <summary>Операция выполнена.</summary>
    Ok,

    /// <summary>Объект не найден — соответствует 404.</summary>
    NotFound,

    /// <summary>Объект принадлежит другому пользователю — соответствует 403.</summary>
    Forbidden,

    /// <summary>В запросе указан несуществующий язык — соответствует 400.</summary>
    InvalidLanguage
}

/// <summary>Результат операции над объектом каталога: статус и, при успехе, данные объекта.</summary>
public sealed record CatalogMutationResult<T>(CatalogMutationStatus Status, T? Value)
{
    /// <summary>Создаёт успешный результат с данными.</summary>
    public static CatalogMutationResult<T> Ok(T value) => new(CatalogMutationStatus.Ok, value);

    /// <summary>Создаёт результат «объект не найден».</summary>
    public static CatalogMutationResult<T> NotFound() => new(CatalogMutationStatus.NotFound, default);

    /// <summary>Создаёт результат «нет прав на объект».</summary>
    public static CatalogMutationResult<T> Forbidden() => new(CatalogMutationStatus.Forbidden, default);

    /// <summary>Создаёт результат «указан несуществующий язык».</summary>
    public static CatalogMutationResult<T> InvalidLanguage() => new(CatalogMutationStatus.InvalidLanguage, default);
}

/// <summary>Курс для панели администратора и кабинета учителя.</summary>
public sealed record AdminCourseItem(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    string Level,
    Guid LanguageId,
    string LanguageCode,
    string? CoverUrl,
    string? AccentColor,
    int EstimatedMinutes,
    bool IsPublished,
    int SortOrder,
    int LessonsCount,
    DateTimeOffset CreatedAt,
    Guid? OwnerUserId = null,
    string? OwnerDisplayName = null);

/// <summary>Урок для панели администратора.</summary>
public sealed record AdminLessonItem(
    Guid Id,
    Guid CourseId,
    string Slug,
    string Title,
    string? Summary,
    string? ContentMarkdown,
    int SortOrder,
    int EstimatedMinutes,
    bool IsPublished,
    Guid? GrammarTopicId,
    string[]? KeyVocabulary,
    LessonGenerationStatus AiGenerationStatus,
    DateTimeOffset? AiGenerationRequestedAt,
    DateTimeOffset? AiGenerationCompletedAt,
    string? AiGenerationError,
    bool IsAvailableToStudents);

/// <summary>Частичное обновление курса: null означает «поле не передано, оставить как есть».</summary>
public static class LessonAvailability
{
    /// <summary>Урок доступен студентам, только если опубликованы и урок, и курс.</summary>
    public static bool IsAvailableToStudents(bool lessonPublished, bool coursePublished) => lessonPublished && coursePublished;
}

public sealed record CourseUpdate(
    string? Slug,
    string? Title,
    string? Description,
    CefrLevel? Level,
    Guid? LanguageId,
    string? CoverUrl,
    string? AccentColor,
    int? EstimatedMinutes,
    bool? IsPublished,
    int? SortOrder);

/// <summary>Частичное обновление урока: null означает «поле не передано, оставить как есть».</summary>
public sealed record LessonUpdate(
    string? Slug,
    string? Title,
    string? Summary,
    string? ContentMarkdown,
    int? SortOrder,
    int? EstimatedMinutes,
    bool? IsPublished,
    Guid? GrammarTopicId,
    string[]? KeyVocabulary);

/// <summary>Строка импортируемых слов, уже разобранная в колонки.</summary>
public sealed record WordImportLine(int LineNumber, string[] Columns);

/// <summary>Параметры массового импорта слов из текста.</summary>
public sealed record WordImportRequest(
    Guid UserId,
    Guid LanguageId,
    Guid TranslationLanguageId,
    string Text,
    string Delimiter,
    bool HasHeader,
    bool SkipDuplicates,
    CefrLevel MinLearnerLevel,
    string[]? DefaultTags,
    Guid? DeckId,
    int MaxWords);

/// <summary>Итог массового импорта слов.</summary>
public sealed record WordImportResult(
    int Imported,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<Guid> CreatedUnitIds);

/// <summary>Количество карточек, готовых к повторению на конкретный день прогноза.</summary>
public sealed record ForecastDayCount(DateOnly Date, int Cards);

/// <summary>Результат добавления слов в колоду.</summary>
public sealed record DeckCardsResult(bool DeckExists, int Added, int DeckSize);

/// <summary>Упражнение для списка и рекомендаций вместе со статистикой попыток пользователя.</summary>
public sealed record ExerciseListItem(
    Guid Id,
    string Type,
    string Title,
    string? Instructions,
    string Level,
    JsonNode? Payload,
    int Points,
    int EstimatedSeconds,
    string Source,
    string? AiProvider,
    string? AiModel,
    Guid? CourseId,
    Guid? LessonId,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    int? BestScorePercent,
    int Attempts);

/// <summary>Агрегированная статистика попыток пользователя по одному упражнению.</summary>
public sealed record ExerciseAttemptStats(int Count, int? BestScorePercent);

/// <summary>Частичное обновление упражнения: null означает «поле не передано, оставить как есть».</summary>
public sealed record ExerciseUpdate(
    string? Title,
    string? Instructions,
    string? Prompt,
    JsonNode? Payload,
    string? ExplanationMarkdown,
    CefrLevel? Level,
    int? Points,
    int? EstimatedSeconds,
    bool? IsPublished,
    bool? IsActive);

/// <summary>Итог операции над упражнением: что вернуть контроллеру в качестве HTTP-ответа.</summary>
public enum ExerciseMutationStatus
{
    /// <summary>Упражнение не найдено — нужен ответ 404.</summary>
    NotFound,

    /// <summary>Недостаточно прав — нужен ответ 403.</summary>
    Forbidden,

    /// <summary>Изменения применены и сохранены.</summary>
    Ok
}

/// <summary>Результат изменения упражнения вместе с сохранённой сущностью.</summary>
public sealed record ExerciseMutationResult(ExerciseMutationStatus Status, Exercise? Exercise);

/// <summary>Строка списка пользователей для панели администратора.</summary>
public sealed record AdminUserItem(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string Level,
    int TotalXp,
    int CurrentStreak,
    bool IsActive,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>Сводная статистика платформы для панели администратора.</summary>
public sealed record PlatformStats(
    int Users,
    int ActiveUsers7d,
    int Languages,
    int Courses,
    int Lessons,
    int Words,
    int WordsWithEmbeddings,
    int Decks,
    int ReviewCards,
    int Reviews7d,
    int Exercises,
    int Attempts7d,
    int ChatSessions,
    int AiCalls7d,
    double AiCost7dUsd);

/// <summary>Расход ИИ-ресурсов по одному провайдеру.</summary>
public sealed record AiUsageByProviderItem(string Provider, int Calls, int InputTokens, int OutputTokens, double EstimatedCostUsd);

/// <summary>Расход ИИ-ресурсов за один день.</summary>
public sealed record AiUsageByDayItem(DateOnly Date, int Calls, int InputTokens, int OutputTokens);

/// <summary>Сводный отчёт по использованию ИИ за выбранный период.</summary>
public sealed record AiUsageReport(
    DateOnly From,
    DateOnly To,
    int TotalCalls,
    int FailedCalls,
    int InputTokens,
    int OutputTokens,
    double EstimatedCostUsd,
    IReadOnlyList<AiUsageByProviderItem> ByProvider,
    IReadOnlyList<AiUsageByDayItem> ByDay);

/// <summary>Файл, загруженный пользователем, с готовым относительным путём для выдачи ссылки.</summary>
public sealed record MediaAssetItem(
    Guid Id,
    string Kind,
    string ContentType,
    long SizeBytes,
    int? DurationMs,
    string? SourceText,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Url);

/// <summary>Статус операции над учебной группой.</summary>
public enum GroupMutationStatus
{
    /// <summary>Операция выполнена.</summary>
    Ok,

    /// <summary>Группа, участник или курс не найден — ответ 404.</summary>
    NotFound,

    /// <summary>Группа принадлежит другому учителю — ответ 403.</summary>
    Forbidden,

    /// <summary>Курс недоступен для назначения группе — ответ 400.</summary>
    InvalidCourse,

    /// <summary>Код-приглашение недействительно — ответ 400.</summary>
    InvalidInvitation
}

/// <summary>Результат операции над учебной группой: что-то, ошибка или запрет доступа.</summary>
public sealed record GroupMutationResult<T>(GroupMutationStatus Status, T? Value)
{
    /// <summary>Результат без значения, когда операция завершилась успешно.</summary>
    public static GroupMutationResult<T> Ok(T value) => new(GroupMutationStatus.Ok, value);

    /// <summary>Результат с признаком «объект не найден».</summary>
    public static GroupMutationResult<T> NotFound() => new(GroupMutationStatus.NotFound, default);

    /// <summary>Результат с признаком «нет прав на операцию».</summary>
    public static GroupMutationResult<T> Forbidden() => new(GroupMutationStatus.Forbidden, default);

    /// <summary>Результат с признаком «курс нельзя назначить этой группе».</summary>
    public static GroupMutationResult<T> InvalidCourse() => new(GroupMutationStatus.InvalidCourse, default);

    /// <summary>Результат с признаком «код-приглашение недействительно».</summary>
    public static GroupMutationResult<T> InvalidInvitation() => new(GroupMutationStatus.InvalidInvitation, default);
}

/// <summary>Краткая карточка группы для списка учителя.</summary>
public sealed record StudyGroupItem(
    Guid Id,
    string Name,
    string? Description,
    int MembersCount,
    int CoursesCount,
    bool IsPersonal,
    DateTimeOffset CreatedAt);

/// <summary>Ученик в составе группы.</summary>
public sealed record StudyGroupMemberItem(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTimeOffset JoinedAt);

/// <summary>Курс, доступный группе.</summary>
public sealed record StudyGroupCourseItem(
    Guid CourseId,
    string Slug,
    string Title,
    string Level,
    string LanguageCode,
    bool IsPublished,
    bool IsOwnCourse,
    DateTimeOffset AssignedAt);

/// <summary>Полное состояние группы: состав, курсы и коды-приглашения.</summary>
public sealed record StudyGroupDetail(
    Guid Id,
    string Name,
    string? Description,
    Guid TeacherUserId,
    bool IsPersonal,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StudyGroupMemberItem> Members,
    IReadOnlyList<StudyGroupCourseItem> Courses,
    IReadOnlyList<GroupInvitationItem> Invitations);

/// <summary>Код-приглашение в группу.</summary>
public sealed record GroupInvitationItem(
    Guid Id,
    string Code,
    DateTimeOffset ExpiresAt,
    int MaxUses,
    int UsedCount,
    bool IsRevoked,
    bool IsActive);

/// <summary>Группа глазами ученика: её учитель и доступные курсы.</summary>
public sealed record LearnerGroupItem(
    Guid Id,
    string Name,
    string? Description,
    string TeacherDisplayName,
    int MembersCount,
    IReadOnlyList<StudyGroupCourseItem> Courses);

/// <summary>Частичное обновление группы: null означает «поле не передано, оставить как есть».</summary>
public sealed record StudyGroupUpdate(string? Name, string? Description);

