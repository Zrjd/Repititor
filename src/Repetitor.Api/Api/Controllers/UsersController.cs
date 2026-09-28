using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Infrastructure;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(
    IUserDbService users,
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
        var current = await LoadAsync(ct);
        if (current is null)
        {
            return Unauthorized();
        }

        var result = await users.UpdateProfileAsync(
            current.Id,
            request.DisplayName,
            request.AvatarUrl,
            request.TargetLanguageId,
            request.InterfaceLanguageId,
            request.Level,
            request.TargetLevel,
            request.DailyGoalXp,
            request.SpeechRate,
            request.PreferredAiProvider,
            request.PreferredChatModel,
            request.WantsCorrectionHints,
            request.PushNotificationsEnabled,
            ct);

        if (result.User is null)
        {
            return Unauthorized();
        }

        if (result.HasInvalidLanguage)
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]>
            {
                [result.InvalidLanguageField!] = ["Язык не найден."]
            });
        }

        return Ok(result.User.ToResponse());
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

        var snapshot = await users.GetDashboardAsync(
            user.Id,
            clock.UtcNow,
            Math.Clamp(activityDays, 1, 90),
            ct);

        return Ok(new DashboardResponse(
            user.ToResponse(),
            snapshot.CardsDue,
            snapshot.NewCards,
            snapshot.TotalWords,
            snapshot.MasteredWords,
            snapshot.ExercisesDone7d,
            snapshot.ReviewsToday,
            snapshot.XpToday,
            user.DailyGoalXp,
            user.CurrentStreak,
            snapshot.Streak,
            snapshot.RecommendedWords
                .Select(w => new DueWordResponse(
                    w.LexicalUnitId, w.Text, w.Translation, w.Transcription,
                    w.AudioUrl, w.State, w.DueInSeconds))
                .ToArray(),
            snapshot.Activity
                .Select(a => new ActivityResponse(
                    a.Date, a.XpEarned, a.ReviewsCompleted, a.CorrectAnswers,
                    a.NewWordsLearned, a.ExercisesCompleted, a.MinutesStudied))
                .ToArray()));
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
        var from = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-Math.Clamp(days, 1, 365) + 1);

        var stats = await users.GetDailyActivityAsync(userId, from, ct);
        return Ok(stats
            .Select(s => new ActivityResponse(
                s.Date, s.XpEarned, s.ReviewsCompleted, s.CorrectAnswers,
                s.NewWordsLearned, s.ExercisesCompleted, s.MinutesStudied))
            .ToArray());
    }

    /// <summary>
    /// Возвращает текущий прогресс по дневной цели XP.
    /// Показывает, сколько XP заработано сегодня, достигнута ли цель,
    /// а также текущую и максимальную серию дней подряд (streak).
    /// Используется для отображения виджета цели на главной странице.
    /// </summary>
    [HttpGet("me/goal")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Goal(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var user = await users.FindAsync(userId, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var stat = await users.GetDailyStatAsync(userId, today, ct);
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

    private async Task<Domain.Entities.User?> LoadAsync(CancellationToken ct) =>
        await users.FindWithLanguagesAsync(CurrentUserAccessor.GetUserId(User), ct);
}
