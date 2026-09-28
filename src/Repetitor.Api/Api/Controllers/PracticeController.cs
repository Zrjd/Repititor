using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/practice")]
[Authorize]
public sealed class PracticeController(
    IPracticeService practice,
    IEmbeddingService embeddings,
    IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
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
        var summary = await practice.BuildSummaryAsync(userId, deckId, 0, 0, 0, 0, 0, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var query = from card in db.ReviewCards
            join ul in db.UserLexicalUnits on card.UserLexicalUnitId equals ul.Id
            where ul.UserId == userId && card.SuspendedAt == null
            select card;

        if (deckId is { } deck)
        {
            var ids = db.DeckCards.Where(dc => dc.DeckId == deck).Select(dc => dc.UserLexicalUnitId);
            query = query.Where(c => ids.Contains(c.UserLexicalUnitId));
        }

        var cards = await query.ToListAsync(ct);
        var forecast = new List<ForecastDayResponse>();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        for (var i = 0; i < 14; i++)
        {
            var date = today.AddDays(i);
            forecast.Add(new ForecastDayResponse(date, cards.Count(c => DateOnly.FromDateTime(c.DueAt.UtcDateTime) <= date)));
        }

        return Ok(new PracticeSummaryResponse(
            0, 0, 0, 0, 0, 0, await practice.CountDueAsync(userId, deckId, ct), forecast));
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

        var forecast = new List<ForecastDayResponse>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var cardQuery = from card in db.ReviewCards
            join ul in db.UserLexicalUnits on card.UserLexicalUnitId equals ul.Id
            where ul.UserId == userId && card.SuspendedAt == null
            select card;

        if (deckId is { } deck)
        {
            var ids = db.DeckCards.Where(dc => dc.DeckId == deck).Select(dc => dc.UserLexicalUnitId);
            cardQuery = cardQuery.Where(c => ids.Contains(c.UserLexicalUnitId));
        }

        var cards = await cardQuery.ToListAsync(ct);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        for (var i = 0; i < 14; i++)
        {
            var date = today.AddDays(i);
            forecast.Add(new ForecastDayResponse(date, cards.Count(c => DateOnly.FromDateTime(c.DueAt.UtcDateTime) <= date)));
        }

        return Ok(new PracticeSummaryResponse(
            summary.Reviewed, summary.Correct, summary.Again, summary.NewCardsSeen, summary.XpEarned,
            summary.AccuracyPercent, summary.RemainingDue, forecast));
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var card = await db.ReviewCards
            .FirstOrDefaultAsync(c => c.Id == reviewCardId && c.UserLexicalUnit!.UserId == userId, ct);

        if (card is null)
        {
            return NotFound();
        }

        card.SuspendedAt = suspended ? DateTimeOffset.UtcNow : null;
        card.UserLexicalUnit!.Suspended = suspended;
        await db.SaveChangesAsync(ct);
        return NoContent();
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.UserLexicalUnits
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.AddedAt)
            .Select(u => u.LexicalUnitId)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);

        await embeddings.EnsureEmbeddingsAsync(ids, null, ct);
        return Ok(new { requested = ids.Count });
    }
}
