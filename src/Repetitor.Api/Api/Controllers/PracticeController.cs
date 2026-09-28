using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/practice")]
[Authorize]
public sealed class PracticeController(
    IPracticeService practice,
    IEmbeddingService embeddings,
    ILexiconDbService lexicon) : ControllerBase
{
    /// <summary>
    /// Возвращает карточки, которые готовы к повторению (due cards).
    /// Используется для начала сессии практики — показывает пользователю, что нужно повторить сейчас.
    /// Можно фильтровать по конкретной колоде через deckId и ограничивать количество через limit.
    /// </summary>
    [HttpGet("due")]
    [ProducesResponseType(typeof(PracticeSessionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PracticeSessionResponse>> Due([FromQuery] Guid? deckId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var cards = await practice.GetDueCardsAsync(userId, deckId, limit, ct);
        var total = await practice.CountDueAsync(userId, deckId, ct);

        return Ok(new PracticeSessionResponse(cards.Select(c => c.ToResponse()).ToArray(), total, cards.Count));
    }

    /// <summary>
    /// Возвращает количество карточек, готовых к повторению.
    /// Нужен для отображения счётчика "сколько карточек ждёт повторения" без загрузки самих карточек.
    /// Полезно для бейджей и уведомлений в интерфейсе.
    /// </summary>
    [HttpGet("due/count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> DueCount([FromQuery] Guid? deckId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        return Ok(new { due = await practice.CountDueAsync(userId, deckId, ct) });
    }

    /// <summary>
    /// Строит прогноз нагрузки на ближайшие 14 дней.
    /// Показывает, сколько карточек будет готово к повторению каждый день, чтобы пользователь мог спланировать своё время.
    /// Используется для визуализации расписания повторений в календаре или графике.
    /// </summary>
    [HttpGet("forecast")]
    [ProducesResponseType(typeof(PracticeSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PracticeSummaryResponse>> Forecast([FromQuery] Guid? deckId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var forecast = await lexicon.BuildForecastAsync(userId, deckId, ForecastDays, DateTimeOffset.UtcNow, ct);

        return Ok(new PracticeSummaryResponse(
            0, 0, 0, 0, 0, 0, await practice.CountDueAsync(userId, deckId, ct), ToForecast(forecast)));
    }

    /// <summary>
    /// Принимает результаты повторения карточек и обновляет их состояние.
    /// Сохраняет оценку пользователя, затраченное время и данный ответ для каждой карточки.
    /// После этого алгоритм интервальных повторений пересчитывает дату следующего показа карточки.
    /// </summary>
    [HttpPost("reviews")]
    [ProducesResponseType(typeof(ReviewResultResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewResultResponse[]>> Submit(ReviewSubmissionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var submissions = request.Reviews
            .Select(r => new ReviewSubmission(r.ReviewCardId, r.Rating, r.DurationMs, r.GivenAnswer))
            .ToArray();

        var results = await practice.SubmitReviewsAsync(userId, submissions, ct);
        return Ok(results.Select(r => r.ToResponse()).ToArray());
    }

    /// <summary>
    /// Возвращает сводку по завершённой сессии практики.
    /// Показывает статистику: сколько карточек повторено, сколько правильных ответов, заработано XP и прогноз на 14 дней.
    /// Используется для экрана результатов после завершения сессии повторения.
    /// </summary>
    [HttpPost("summary")]
    [ProducesResponseType(typeof(PracticeSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PracticeSummaryResponse>> Summary([FromQuery] Guid? deckId, [FromQuery] int reviewed = 0, [FromQuery] int correct = 0, [FromQuery] int again = 0, [FromQuery] int newSeen = 0, [FromQuery] int xp = 0, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var summary = await practice.BuildSummaryAsync(userId, deckId, reviewed, correct, again, newSeen, xp, ct);
        var forecast = await lexicon.BuildForecastAsync(userId, deckId, ForecastDays, DateTimeOffset.UtcNow, ct);

        return Ok(new PracticeSummaryResponse(
            summary.Reviewed, summary.Correct, summary.Again, summary.NewCardsSeen, summary.XpEarned,
            summary.AccuracyPercent, summary.RemainingDue, ToForecast(forecast)));
    }

    /// <summary>
    /// Приостанавливает или возобновляет показ карточки в повторениях.
    /// Приостановленная карточка не будет появляться в сессиях практики, пока пользователь не снимет приостановку.
    /// Полезно, когда пользователь хочет временно исключить слово из повторения без удаления.
    /// </summary>
    [HttpPost("suspend/{reviewCardId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(Guid reviewCardId, [FromQuery] bool suspended = true, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var updated = await lexicon.SetCardSuspensionAsync(userId, reviewCardId, suspended, DateTimeOffset.UtcNow, ct);
        return updated ? NoContent() : NotFound();
    }

    /// <summary>
    /// Предварительно вычисляет и сохраняет векторные представления (embeddings) для последних добавленных слов пользователя.
    /// Нужен для ускорения семантического поиска — без предварительного вычисления поиск будет медленным.
    /// Рекомендуется вызывать после массового импорта слов или добавления новых карточек.
    /// </summary>
    [HttpPost("prefetch-embeddings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Prefetch([FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var ids = await lexicon.GetRecentUnitIdsAsync(userId, Math.Clamp(limit, 1, 1000), ct);

        await embeddings.EnsureEmbeddingsAsync(ids, null, ct);
        return Ok(new { requested = ids.Count });
    }

    /// <summary>Горизонт прогноза повторений: столько дней вперёд показывается пользователю.</summary>
    private const int ForecastDays = 14;

    /// <summary>Преобразует подсчитанный прогноз в форму, которую ожидает клиент.</summary>
    private static ForecastDayResponse[] ToForecast(IReadOnlyList<ForecastDayCount> forecast) =>
        forecast.Select(f => new ForecastDayResponse(f.Date, f.Cards)).ToArray();
}
