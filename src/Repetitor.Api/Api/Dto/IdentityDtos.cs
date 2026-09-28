using System.ComponentModel.DataAnnotations;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Api.Dto;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages = 0)
{
    public int TotalPages { get; init; } = TotalPages <= 0
        ? PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize)
        : TotalPages;

    public static PagedResponse<T> Create(IReadOnlyList<T> items, int page, int pageSize, int total) =>
        new(items, page, pageSize, total);
}

public sealed class PagedRequest
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 20;
}

public sealed record ErrorResponse(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresInSeconds,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string? AvatarUrl,
    Guid TargetLanguageId,
    string TargetLanguageCode,
    string TargetLanguageName,
    Guid InterfaceLanguageId,
    string Level,
    string TargetLevel,
    int DailyGoalXp,
    double SpeechRate,
    int TotalXp,
    int CurrentStreak,
    int LongestStreak,
    string? PreferredAiProvider,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt);

public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(320)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(120, MinimumLength = 2)] public string DisplayName { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
    public Guid TargetLanguageId { get; set; }
    public Guid InterfaceLanguageId { get; set; }
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    public int DailyGoalXp { get; set; } = 50;

    /// <summary>
    /// Роль нового пользователя. Разрешены только Learner и Teacher;
    /// значение Admin отклоняется сервером, чтобы его нельзя было получить при регистрации.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Learner;
}

public sealed class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

public sealed class UpdateProfileRequest
{
    [StringLength(120)] public string? DisplayName { get; set; }
    [StringLength(512)] public string? AvatarUrl { get; set; }
    public Guid? TargetLanguageId { get; set; }
    public Guid? InterfaceLanguageId { get; set; }
    public CefrLevel? Level { get; set; }
    public CefrLevel? TargetLevel { get; set; }
    [Range(5, 1000)] public int? DailyGoalXp { get; set; }
    [Range(0.5, 2.0)] public double? SpeechRate { get; set; }
    [StringLength(64)] public string? PreferredAiProvider { get; set; }
    [StringLength(128)] public string? PreferredChatModel { get; set; }
    public bool? WantsCorrectionHints { get; set; }
    public bool? PushNotificationsEnabled { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required] public string Token { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}

public sealed record LanguageResponse(
    Guid Id,
    string Code,
    string NameEnglish,
    string NameRussian,
    string? NativeName,
    string? FlagEmoji,
    bool IsEnabled);

public sealed record CourseResponse(
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
    int LessonsCount,
    int? ProgressPercent,
    bool IsEnrolled);

public sealed record LessonResponse(
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

public sealed record GrammarTopicResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string MinLevel,
    string? ExplanationMarkdown);

public sealed record DashboardResponse(
    UserResponse User,
    int CardsDue,
    int NewCards,
    int TotalWords,
    int MasteredWords,
    int ExercisesDone7d,
    int ReviewsToday,
    int XpToday,
    int DailyGoalXp,
    int CurrentStreak,
    int[] StreakLast14,
    IReadOnlyList<DueWordResponse> RecommendedWords,
    IReadOnlyList<ActivityResponse> Activity);

public sealed record DueWordResponse(
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    string? AudioUrl,
    string State,
    int DueInSeconds);

public sealed record ActivityResponse(
    DateOnly Date,
    int XpEarned,
    int ReviewsCompleted,
    int CorrectAnswers,
    int NewWordsLearned,
    int ExercisesCompleted,
    int MinutesStudied);
