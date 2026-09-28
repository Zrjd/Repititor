using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IUserDbService"/> поверх фабрики контекстов EF Core.
/// Каждый метод сам создаёт и освобождает контекст, поэтому сервис можно регистрировать как Scoped.
/// </summary>
public sealed class UserDbService(IDbContextFactory<AppDbContext> dbFactory) : IUserDbService
{
    public async Task<User?> FindByEmailAsync(string email, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<User?> FindActiveByEmailAsync(string email, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);
    }

    public async Task<User?> FindWithLanguagesAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    public async Task<User?> FindAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(u => u.Email == email, ct);
    }

    public async Task<User> CreateWithFirstDeckAsync(
        User user,
        string firstDeckName,
        string firstDeckDescription,
        string languageCode,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Users.Add(user);
        db.Decks.Add(new Deck
        {
            UserId = user.Id,
            Name = firstDeckName,
            Description = firstDeckDescription,
            LanguageCode = languageCode
        });

        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task TouchLastLoginAsync(Guid userId, DateTimeOffset now, string? newPasswordHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return;
        }

        user.LastLoginAt = now;
        if (newPasswordHash is not null)
        {
            user.PasswordHash = newPasswordHash;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<UserProfileUpdateResult> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? avatarUrl,
        Guid? targetLanguageId,
        Guid? interfaceLanguageId,
        CefrLevel? level,
        CefrLevel? targetLevel,
        int? dailyGoalXp,
        double? speechRate,
        string? preferredAiProvider,
        string? preferredChatModel,
        bool? wantsCorrectionHints,
        bool? pushNotificationsEnabled,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return new UserProfileUpdateResult(null, null);
        }

        // Языки проверяем до правок, чтобы несохранённый профиль не менялся ни при каких условиях.
        if (targetLanguageId is { } target && target != Guid.Empty
            && !await db.Languages.AnyAsync(l => l.Id == target, ct))
        {
            return new UserProfileUpdateResult(null, nameof(targetLanguageId));
        }

        if (interfaceLanguageId is { } uiLanguage && uiLanguage != Guid.Empty
            && !await db.Languages.AnyAsync(l => l.Id == uiLanguage, ct))
        {
            return new UserProfileUpdateResult(null, nameof(interfaceLanguageId));
        }

        if (displayName is not null) user.DisplayName = displayName.Trim();
        if (avatarUrl is not null) user.AvatarUrl = avatarUrl;
        if (targetLanguageId is { } tl && tl != Guid.Empty) user.TargetLanguageId = tl;
        if (interfaceLanguageId is { } il && il != Guid.Empty) user.InterfaceLanguageId = il;
        if (level is { } newLevel) user.Level = newLevel;
        if (targetLevel is { } newTargetLevel) user.TargetLevel = newTargetLevel;
        if (dailyGoalXp is { } goal) user.DailyGoalXp = goal;
        if (speechRate is { } rate) user.SpeechRate = rate;
        if (preferredAiProvider is not null) user.PreferredAiProvider = preferredAiProvider;
        if (preferredChatModel is not null) user.PreferredChatModel = preferredChatModel;
        if (wantsCorrectionHints is { } hints) user.WantsCorrectionHints = hints;
        if (pushNotificationsEnabled is { } push) user.PushNotificationsEnabled = push;

        await db.SaveChangesAsync(ct);
        return new UserProfileUpdateResult(user, null);
    }

    public async Task SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return;
        }

        user.PasswordHash = passwordHash;
        await db.SaveChangesAsync(ct);
    }

    public async Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u!.TargetLanguage)
            .Include(t => t.User).ThenInclude(u => u!.InterfaceLanguage)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public async Task<RefreshToken> AddRefreshTokenAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            UserAgent = userAgent,
            IpAddress = ipAddress
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task RotateRefreshTokenAsync(
        Guid currentTokenId,
        Guid userId,
        string newTokenHash,
        DateTimeOffset expiresAt,
        string? userAgent,
        string? ipAddress,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == currentTokenId, ct);
        if (current is null)
        {
            return;
        }

        current.RevokedAt = now;
        current.RevokedByRotation = true;

        var replacement = new RefreshToken
        {
            UserId = userId,
            TokenHash = newTokenHash,
            ExpiresAt = expiresAt,
            UserAgent = userAgent,
            IpAddress = ipAddress,
            ReplacedByTokenId = null
        };
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        // Ссылку на новый токен ставим вторым сохранением, потому что его идентификатор
        // появляется только после вставки в базу.
        current.ReplacedByTokenId = replacement.Id;
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeRefreshTokenAsync(Guid userId, string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.TokenHash == tokenHash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task RevokeAllRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task<PasswordResetToken?> FindPasswordResetTokenAsync(string tokenHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.UsedAt == null, ct);
    }

    public async Task AddPasswordResetTokenAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task CompletePasswordResetAsync(Guid tokenId, Guid userId, string passwordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var token = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.Id == tokenId, ct);
        if (user is null || token is null)
        {
            return;
        }

        user.PasswordHash = passwordHash;
        token.UsedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserDashboardSnapshot> GetDashboardAsync(Guid userId, DateTimeOffset now, int activityDays, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var wordsTotal = await db.UserLexicalUnits.CountAsync(u => u.UserId == userId, ct);
        var wordsMastered = await db.UserLexicalUnits.CountAsync(u => u.UserId == userId && u.State == CardState.Mastered, ct);
        var dueQuery = db.ReviewCards
            .Where(c => c.UserLexicalUnit!.UserId == userId && c.SuspendedAt == null && c.DueAt <= now);
        var cardsDue = await dueQuery.CountAsync(ct);
        var newCards = await db.ReviewCards
            .Where(c => c.UserLexicalUnit!.UserId == userId && c.State == CardState.New)
            .CountAsync(ct);
        var exercisesWeek = await db.ExerciseAttempts
            .CountAsync(a => a.UserId == userId && a.CompletedAt >= now.AddDays(-7), ct);

        var stats = await db.UserDailyStats
            .Where(s => s.UserId == userId && s.Date >= today.AddDays(-activityDays + 1))
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        var todayStat = stats.FirstOrDefault(s => s.Date == today);

        // Массив отмечает дни, в которые был заработан хотя бы один XP, — по нему рисуется «полоса» активности.
        var streak = new int[activityDays];
        for (var i = 0; i < activityDays; i++)
        {
            var date = today.AddDays(i - activityDays + 1);
            streak[i] = stats.FirstOrDefault(s => s.Date == date) is { XpEarned: > 0 } ? 1 : 0;
        }

        var recommended = await dueQuery
            .OrderBy(c => c.DueAt)
            .Take(10)
            .Select(c => new DueWordItem(
                c.UserLexicalUnit!.LexicalUnitId,
                c.UserLexicalUnit.LexicalUnit!.Text,
                c.UserLexicalUnit.LexicalUnit.Translation,
                c.UserLexicalUnit.LexicalUnit.Transcription,
                c.UserLexicalUnit.LexicalUnit.AudioUrl,
                c.State.ToString(),
                (int)(c.DueAt - now).TotalSeconds))
            .ToListAsync(ct);

        var activity = stats
            .Select(s => new DailyActivityItem(
                s.Date, s.XpEarned, s.ReviewsCompleted, s.CorrectAnswers,
                s.NewWordsLearned, s.ExercisesCompleted, s.MinutesStudied))
            .ToArray();

        return new UserDashboardSnapshot(
            cardsDue, newCards, wordsTotal, wordsMastered, exercisesWeek,
            todayStat?.ReviewsCompleted ?? 0, todayStat?.XpEarned ?? 0,
            streak, recommended, activity);
    }

    public async Task<IReadOnlyList<DailyActivityItem>> GetDailyActivityAsync(Guid userId, DateOnly from, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserDailyStats
            .Where(s => s.UserId == userId && s.Date >= from)
            .OrderBy(s => s.Date)
            .Select(s => new DailyActivityItem(
                s.Date, s.XpEarned, s.ReviewsCompleted, s.CorrectAnswers,
                s.NewWordsLearned, s.ExercisesCompleted, s.MinutesStudied))
            .ToListAsync(ct);
    }

    public async Task<UserDailyStat?> GetDailyStatAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserDailyStats.FirstOrDefaultAsync(s => s.UserId == userId && s.Date == date, ct);
    }
}
