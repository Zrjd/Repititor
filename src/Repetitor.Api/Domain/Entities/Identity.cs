using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Learner;
    public string? AvatarUrl { get; set; }

    public Guid InterfaceLanguageId { get; set; }
    public Language? InterfaceLanguage { get; set; }
    public Guid TargetLanguageId { get; set; }
    public Language? TargetLanguage { get; set; }
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    public CefrLevel TargetLevel { get; set; } = CefrLevel.B1;

    public int DailyGoalXp { get; set; } = 50;
    public double SpeechRate { get; set; } = 1.0;
    public string? PreferredAiProvider { get; set; }
    public string? PreferredChatModel { get; set; }
    public bool WantsCorrectionHints { get; set; } = true;
    public bool PushNotificationsEnabled { get; set; } = true;

    public int TotalXp { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateOnly? LastActivityDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public int AiCallsToday { get; set; }
    public DateOnly? AiCallsDate { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<UserLexicalUnit> LexicalUnits { get; set; } = [];
    public ICollection<Deck> Decks { get; set; } = [];
    public ICollection<CourseEnrollment> Enrollments { get; set; } = [];
    public ICollection<UserDailyStat> DailyStats { get; set; } = [];
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public bool RevokedByRotation { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UsedAt { get; set; }
}
