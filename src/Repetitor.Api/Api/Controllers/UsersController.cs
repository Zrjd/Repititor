using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Infrastructure;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(
    IDbContextFactory<AppDbContext> dbFactory,
    IClock clock,
    IOptions<LearningOptions> learningOptions) : ControllerBase
{
    /// <summary>
    /// Возвращает полный профиль текущего аутентифицированного пользователя.
    /// Включает информацию о языках обучения, уровне, статистике и настройках.
    /// Используется для отображения личного кабинета и страницы настроек.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> GetProfile(CancellationToken ct)
    {
        var user = await LoadAsync(ct);
        return user is null ? Unauthorized() : Ok(user.ToResponse());
    }

    /// <summary>
    /// Обновляет настройки профиля текущего пользователя.
    /// Позволяет изменить отображаемое имя, аватар, языки обучения,
    /// уровень, дневную цель XP, скорость речи и другие параметры.
    /// Принимает только переданные поля — остальные остаются без изменений.
    /// </summary>
    [HttpPatch("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await LoadAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstAsync(u => u.Id == user.Id, ct);

        if (request.DisplayName is not null)
        {
            tracked.DisplayName = request.DisplayName.Trim();
        }

        if (request.AvatarUrl is not null)
        {
            tracked.AvatarUrl = request.AvatarUrl;
        }

        if (request.TargetLanguageId is { } tl && tl != Guid.Empty)
        {
            if (!await db.Languages.AnyAsync(l => l.Id == tl, ct))
            {
                return ApiValidation.Invalid(new Dictionary<string, string[]> { ["targetLanguageId"] = ["Язык не найден."] });
            }

            tracked.TargetLanguageId = tl;
        }

        if (request.InterfaceLanguageId is { } il && il != Guid.Empty)
        {
            if (!await db.Languages.AnyAsync(l => l.Id == il, ct))
            {
                return ApiValidation.Invalid(new Dictionary<string, string[]> { ["interfaceLanguageId"] = ["Язык не найден."] });
            }

            tracked.InterfaceLanguageId = il;
        }

        if (request.Level is { } level)
        {
            tracked.Level = level;
        }

        if (request.TargetLevel is { } targetLevel)
        {
            tracked.TargetLevel = targetLevel;
        }

        if (request.DailyGoalXp is { } goal)
        {
            tracked.DailyGoalXp = goal;
        }

        if (request.SpeechRate is { } rate)
        {
            tracked.SpeechRate = rate;
        }

        if (request.PreferredAiProvider is not null)
        {
            tracked.PreferredAiProvider = request.PreferredAiProvider;
        }

        if (request.PreferredChatModel is not null)
        {
            tracked.PreferredChatModel = request.PreferredChatModel;
        }

        if (request.WantsCorrectionHints is { } hints)
        {
            tracked.WantsCorrectionHints = hints;
        }

        if (request.PushNotificationsEnabled is { } push)
        {
            tracked.PushNotificationsEnabled = push;
        }

        await db.SaveChangesAsync(ct);
        await db.Entry(tracked).Reference(u => u.TargetLanguage).LoadAsync(ct);
        await db.Entry(tracked).Reference(u => u.InterfaceLanguage).LoadAsync(ct);
        return Ok(tracked.ToResponse());
    }

    /// <summary>
    /// Возвращает агрегированные данные для дашборда пользователя.
    /// Включает статистику изучения слов, количество карточек к повторению,
    /// активность за период, рекомендуемые слова и прогресс по дневной цели.
    /// Параметр activityDays определяет глубину истории (1–90 дней).
    /// </summary>
    [HttpGet("me/dashboard")]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardResponse>> Dashboard([FromQuery] int activityDays = 14, CancellationToken ct = default)
    {
        var user = await LoadAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var userId = user.Id;
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        activityDays = Math.Clamp(activityDays, 1, 90);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var wordsTotal = await db.UserLexicalUnits.CountAsync(u => u.UserId == userId, ct);
        var wordsMastered = await db.UserLexicalUnits.CountAsync(u => u.UserId == userId && u.State == CardState.Mastered, ct);
        var cardsDue = await db.ReviewCards
            .Where(c => c.UserLexicalUnit!.UserId == userId && c.SuspendedAt == null && c.DueAt <= now)
            .CountAsync(ct);
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

        var streak = new int[activityDays];
        for (var i = 0; i < activityDays; i++)
        {
            var date = today.AddDays(i - activityDays + 1);
            streak[i] = stats.FirstOrDefault(s => s.Date == date) is { XpEarned: > 0 } ? 1 : 0;
        }

        var recommended = await db.ReviewCards
            .Where(c => c.UserLexicalUnit!.UserId == userId && c.SuspendedAt == null && c.DueAt <= now)
            .OrderBy(c => c.DueAt)
            .Take(10)
            .Select(c => new DueWordResponse(
                c.UserLexicalUnit!.LexicalUnitId,
                c.UserLexicalUnit.LexicalUnit!.Text,
                c.UserLexicalUnit.LexicalUnit.Translation,
                c.UserLexicalUnit.LexicalUnit.Transcription,
                c.UserLexicalUnit.LexicalUnit.AudioUrl,
                c.State.ToString(),
                (int)(c.DueAt - now).TotalSeconds))
            .ToListAsync(ct);

        var activity = stats.Select(s => new ActivityResponse(
            s.Date, s.XpEarned, s.ReviewsCompleted, s.CorrectAnswers,
            s.NewWordsLearned, s.ExercisesCompleted, s.MinutesStudied)).ToArray();

        return Ok(new DashboardResponse(
            user.ToResponse(),
            cardsDue,
            newCards,
            wordsTotal,
            wordsMastered,
            exercisesWeek,
            todayStat?.ReviewsCompleted ?? 0,
            todayStat?.XpEarned ?? 0,
            user.DailyGoalXp,
            user.CurrentStreak,
            streak,
            recommended,
            activity));
    }

    /// <summary>
    /// Возвращает ежедневную статистику активности пользователя за указанный период.
    /// Содержит данные о заработанном XP, количестве повторений, правильных ответах,
    /// новых словах и времени обучения для каждого дня. Используется для графиков
    /// и визуализации прогресса обучения.
    /// </summary>
    [HttpGet("me/stats")]
    [ProducesResponseType(typeof(ActivityResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ActivityResponse[]>> Stats([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        days = Math.Clamp(days, 1, 365);
        var from = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-days + 1);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stats = await db.UserDailyStats
            .Where(s => s.UserId == userId && s.Date >= from)
            .OrderBy(s => s.Date)
            .Select(s => new ActivityResponse(s.Date, s.XpEarned, s.ReviewsCompleted, s.CorrectAnswers,
                s.NewWordsLearned, s.ExercisesCompleted, s.MinutesStudied))
            .ToListAsync(ct);

        return Ok(stats);
    }

    /// <summary>
    /// Возвращает текущий прогресс по дневной цели XP.
    /// Показывает, сколько XP заработано сегодня, достигнута ли цель,
    /// а также текущую и максимальную серию дней подряд (streak).
    /// Используется для отображения виджета цели на главной странице.
    /// </summary>
    [HttpGet("me/goal")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Goal(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var stat = await db.UserDailyStats.FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today, ct);
        var goal = learningOptions.Value.DefaultDailyGoalXp;

        return Ok(new
        {
            dailyGoalXp = user.DailyGoalXp > 0 ? user.DailyGoalXp : goal,
            xpToday = stat?.XpEarned ?? 0,
            reviewsToday = stat?.ReviewsCompleted ?? 0,
            goalReached = (stat?.XpEarned ?? 0) >= user.DailyGoalXp,
            currentStreak = user.CurrentStreak,
            longestStreak = user.LongestStreak
        });
    }

    private async Task<Domain.Entities.User?> LoadAsync(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
    }
}
