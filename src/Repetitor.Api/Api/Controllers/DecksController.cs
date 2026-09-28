using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/decks")]
[Authorize]
public sealed class DecksController(
    ILexiconDbService lexicon,
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
        var now = clock.UtcNow;
        var decks = await lexicon.GetDecksAsync(CurrentUserAccessor.GetUserId(User), includeArchived, ct);

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
        var now = clock.UtcNow;
        var deck = await lexicon.FindDeckAsync(CurrentUserAccessor.GetUserId(User), id, ct);
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
    public async Task<ActionResult<PagedResponse<UserWordResponse>>> Cards(Guid id, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        var result = await lexicon.GetDeckCardsAsync(CurrentUserAccessor.GetUserId(User), id, request.Page, request.PageSize, ct);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(PagedResponse<UserWordResponse>.Create(
            result.Items.Select(r => r.ToResponse()).ToArray(), result.Page, result.PageSize, result.Total));
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
        var deck = new Deck
        {
            UserId = CurrentUserAccessor.GetUserId(User),
            Name = TextNormalizer.Collapse(request.Name),
            Description = request.Description,
            LanguageCode = request.LanguageCode,
            CoverEmoji = request.CoverEmoji,
            Tags = request.Tags
        };

        var created = await lexicon.CreateDeckAsync(deck, request.LexicalUnitIds, _learning.MaxDeckSize, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created.ToResponse(0, 0, 0));
    }

    /// <summary>
    /// Обновляет название, описание или другие параметры колоды.
    /// Обновляются только те поля, которые переданы в запросе.
    /// Также позволяет архивировать колоду (IsArchived), чтобы скрыть её из списка активных.
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(DeckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeckResponse>> Update(Guid id, UpdateDeckRequest request, CancellationToken ct)
    {
        var deck = await lexicon.UpdateDeckAsync(
            CurrentUserAccessor.GetUserId(User), id,
            request.Name, request.Description, request.LanguageCode,
            request.CoverEmoji, request.Tags, request.IsArchived, ct);

        return deck is null ? NotFound() : Ok(deck.ToResponse(0, 0, 0));
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
        var deleted = await lexicon.DeleteDeckAsync(CurrentUserAccessor.GetUserId(User), id, ct);
        return deleted ? NoContent() : NotFound();
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
        var result = await lexicon.AddCardsToDeckAsync(
            CurrentUserAccessor.GetUserId(User), id, request.LexicalUnitIds, _learning.MaxDeckSize, ct);

        if (!result.DeckExists)
        {
            return NotFound();
        }

        return Ok(new { added = result.Added, requested = request.LexicalUnitIds.Length, deckSize = result.DeckSize });
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
        var removed = await lexicon.RemoveDeckCardAsync(CurrentUserAccessor.GetUserId(User), id, lexicalUnitId, ct);
        return removed ? NoContent() : NotFound();
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
        await lexicon.ResetDeckProgressAsync(id, clock.UtcNow, ct);
        return NoContent();
    }
}
