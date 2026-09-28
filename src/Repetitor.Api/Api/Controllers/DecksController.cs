using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/decks")]
[Authorize]
public sealed class DecksController(
    IDbContextFactory<AppDbContext> dbFactory,
    IClock clock,
    IOptions<LearningOptions> learningOptions) : ControllerBase
{
    private readonly LearningOptions _learning = learningOptions.Value;

    /// <summary>
    /// Возвращает список колод текущего пользователя.
    /// По умолчанию показывает только активные колоды; при includeArchived = true включает и архивные.
    /// Для каждой колоды подсчитывает количество карточек к повторению, новых и выученных.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(DeckResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<DeckResponse[]>> List([FromQuery] bool includeArchived = false, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var now = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var decks = await db.Decks
            .Include(d => d.Cards).ThenInclude(c => c.UserLexicalUnit).ThenInclude(u => u!.ReviewCard)
            .Where(d => d.UserId == userId && (includeArchived || !d.IsArchived))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        return Ok(decks.Select(d => d.ToResponse(
            d.Cards.Count(c => c.UserLexicalUnit!.ReviewCard is { SuspendedAt: null } rc && rc.DueAt <= now),
            d.Cards.Count(c => c.UserLexicalUnit!.ReviewCard is { State: CardState.New }),
            d.Cards.Count(c => c.UserLexicalUnit!.State == CardState.Mastered))).ToArray());
    }

    /// <summary>
    /// Возвращает подробную информацию о конкретной колоде.
    /// Включает статистику: сколько карточек готово к повторению, сколько новых и сколько уже выучено.
    /// Используется для страницы просмотра колоды перед началом практики.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(DeckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeckResponse>> Get(Guid id, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var now = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deck = await db.Decks
            .Include(d => d.Cards).ThenInclude(c => c.UserLexicalUnit).ThenInclude(u => u!.ReviewCard)
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);

        if (deck is null)
        {
            return NotFound();
        }

        return Ok(deck.ToResponse(
            deck.Cards.Count(c => c.UserLexicalUnit!.ReviewCard is { SuspendedAt: null } rc && rc.DueAt <= now),
            deck.Cards.Count(c => c.UserLexicalUnit!.ReviewCard is { State: CardState.New }),
            deck.Cards.Count(c => c.UserLexicalUnit!.State == CardState.Mastered)));
    }

    /// <summary>
    /// Возвращает список карточек (слов) в указанной колоде с пагинацией.
    /// Карточки сортируются по позиции в колоде (порядку добавления).
    /// Используется для просмотра содержимого колоды.
    /// </summary>
    [HttpGet("{id}/cards")]
    [ProducesResponseType(typeof(UserWordResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserWordResponse[]>> Cards(Guid id, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var exists = await db.Decks.AnyAsync(d => d.Id == id && d.UserId == userId, ct);
        if (!exists)
        {
            return NotFound();
        }

        var query = db.DeckCards
            .Where(dc => dc.DeckId == id)
            .OrderBy(dc => dc.Position);

        var total = await query.CountAsync(ct);
        var page = Math.Max(request.Page, 1);
        var rows = await query
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(dc => dc.UserLexicalUnit!)
            .ToListAsync(ct);

        return Ok(PagedResponse<UserWordResponse>.Create(
            rows.Select(r => r.ToResponse()).ToArray(), page, request.PageSize, total));
    }

    /// <summary>
    /// Создаёт новую колоду карточек.
    /// В запросе можно указать название, описание, язык, обложку (эмодзи) и теги.
    /// Также можно сразу передать список слов, которые будут добавлены в колоду.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DeckResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<DeckResponse>> Create(CreateDeckRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var deck = new Deck
        {
            UserId = userId,
            Name = TextNormalizer.Collapse(request.Name),
            Description = request.Description,
            LanguageCode = request.LanguageCode,
            CoverEmoji = request.CoverEmoji,
            Tags = request.Tags
        };
        db.Decks.Add(deck);
        await db.SaveChangesAsync(ct);

        if (request.LexicalUnitIds is { Length: > 0 })
        {
            await AddCardsInternalAsync(db, userId, deck.Id, request.LexicalUnitIds, ct);
        }

        return CreatedAtAction(nameof(Get), new { id = deck.Id }, deck.ToResponse(0, 0, 0));
    }

    /// <summary>
    /// Обновляет название, описание или другие параметры колоды.
    /// Обновляются только те поля, которые переданы в запросе.
    /// Также позволяет архивировать колоду (IsArchived), чтобы скрыть её из списка активных.
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(DeckResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DeckResponse>> Update(Guid id, UpdateDeckRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (deck is null)
        {
            return NotFound();
        }

        if (request.Name is not null) deck.Name = TextNormalizer.Collapse(request.Name);
        if (request.Description is not null) deck.Description = request.Description;
        if (request.LanguageCode is not null) deck.LanguageCode = request.LanguageCode;
        if (request.CoverEmoji is not null) deck.CoverEmoji = request.CoverEmoji;
        if (request.Tags is not null) deck.Tags = request.Tags;
        if (request.IsArchived is { } archived) deck.IsArchived = archived;

        await db.SaveChangesAsync(ct);
        return Ok(deck.ToResponse(0, 0, 0));
    }

    /// <summary>
    /// Полностью удаляет колоду вместе со всеми связями с карточками.
    /// Сами слова при этом не удаляются из словаря и личного словаря пользователя.
    /// Действие необратимо — колоду восстановить будет нельзя.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (deck is null)
        {
            return NotFound();
        }

        db.Decks.Remove(deck);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Добавляет слова в существующую колоду.
    /// Возвращает количество фактически добавленных карточек (без дубликатов) и общий размер колоды.
    /// Если слово уже есть у пользователя, оно будет привязано к колоде; если нет — будет добавлено в личный словарь.
    /// </summary>
    [HttpPost("{id}/cards")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCards(Guid id, DeckCardsRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (deck is null)
        {
            return NotFound();
        }

        var added = await AddCardsInternalAsync(db, userId, id, request.LexicalUnitIds, ct);
        return Ok(new { added, requested = request.LexicalUnitIds.Length, deckSize = await db.DeckCards.CountAsync(dc => dc.DeckId == id) });
    }

    /// <summary>
    /// Удаляет карточку из колоды, но не из словаря.
    /// Слово остаётся в личном словаре пользователя и продолжит участвовать в повторениях, просто исчезнет из этой колоды.
    /// Используется для наполнения колоды только теми словами, которые пользователь хочет повторять в данном контексте.
    /// </summary>
    [HttpDelete("{id}/cards/{lexicalUnitId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveCard(Guid id, Guid lexicalUnitId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var card = await db.DeckCards
            .Where(dc => dc.DeckId == id && dc.UserLexicalUnit!.LexicalUnitId == lexicalUnitId && dc.Deck!.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (card is null)
        {
            return NotFound();
        }

        db.DeckCards.Remove(card);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Сбрасывает прогресс изучения всех карточек в колоде.
    /// Все интервалы, повторения и статистика обнуляются — карточки становятся "новыми" и готовы к повторению с нуля.
    /// Используется, если пользователь хочет начать изучение колоды заново.
    /// </summary>
    [HttpPost("{id}/reset-progress")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetProgress(Guid id, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var now = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var unitIds = db.DeckCards.Where(dc => dc.DeckId == id).Select(dc => dc.UserLexicalUnitId);
        var cards = await db.ReviewCards.Where(rc => unitIds.Contains(rc.UserLexicalUnitId)).ToListAsync(ct);
        foreach (var card in cards)
        {
            card.State = CardState.New;
            card.IntervalDays = 0;
            card.EaseFactor = 2.5;
            card.Repetitions = 0;
            card.Lapses = 0;
            card.LearningStep = 0;
            card.DueAt = now;
            card.LastReviewedAt = null;
        }

        var entries = await db.UserLexicalUnits.Where(u => unitIds.Contains(u.Id)).ToListAsync(ct);
        foreach (var entry in entries)
        {
            entry.State = CardState.New;
            entry.MasteryScore = 0;
            entry.CorrectStreak = 0;
            entry.IncorrectStreak = 0;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<int> AddCardsInternalAsync(AppDbContext db, Guid userId, Guid deckId, IReadOnlyCollection<Guid> lexicalUnitIds, CancellationToken ct)
    {
        var deckSize = await db.DeckCards.CountAsync(dc => dc.DeckId == deckId, ct);
        if (deckSize + lexicalUnitIds.Count > _learning.MaxDeckSize)
        {
            throw new InvalidOperationException($"Достигнут лимит колоды: {_learning.MaxDeckSize} карточек.");
        }

        var added = 0;
        foreach (var lexicalUnitId in lexicalUnitIds.Distinct())
        {
            var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == lexicalUnitId, ct);
            if (unit is null)
            {
                continue;
            }

            var entry = await DictionaryController.AttachToUserAsync(db, userId, unit, deckId, ContentSource.UserCreated);
            entry.LastDeckId = deckId;

            var exists = await db.DeckCards.AnyAsync(dc => dc.DeckId == deckId && dc.UserLexicalUnitId == entry.Id, ct);
            if (exists)
            {
                continue;
            }

            var position = await db.DeckCards.CountAsync(dc => dc.DeckId == deckId, ct);
            db.DeckCards.Add(new DeckCard { DeckId = deckId, UserLexicalUnitId = entry.Id, Position = position });
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
