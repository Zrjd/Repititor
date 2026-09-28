using System.Text;
using System.Text.Json;
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
[Route("api/v1/dictionary")]
[Authorize]
public sealed class DictionaryController(
    ILexiconDbService lexicon,
    ICatalogDbService catalog,
    IUserDbService users,
    IVectorSearchService vectorSearch,
    IEmbeddingService embeddings,
    IClock clock,
    IOptions<LearningOptions> learningOptions) : ControllerBase
{
    private readonly LearningOptions _learning = learningOptions.Value;

    /// <summary>
    /// Ищет слова в словаре с фильтрацией и пагинацией.
    /// Поддерживает два режима: обычный текстовый поиск (по префиксу или вхождению) и семантический поиск (по смыслу через векторные представления).
    /// Можно фильтровать по уровню сложности, части речи и тегам.
    /// </summary>
    [HttpGet("words")]
    [ProducesResponseType(typeof(PagedResponse<LexicalUnitResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<LexicalUnitResponse>>> Search([FromQuery] SearchWordsRequest request, CancellationToken ct)
    {
        var user = await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageId = request.LanguageId ?? user.TargetLanguageId;
        var translationLanguageId = request.TranslationLanguageId ?? user.InterfaceLanguageId;

        if (!string.IsNullOrWhiteSpace(request.Query) && request.Semantic)
        {
            // Семантический поиск сам выбирает порядок выдачи, поэтому пагинация применяется к числу кандидатов.
            var matches = await vectorSearch.SearchAsync(
                request.Query!, languageId, translationLanguageId, request.Provider,
                Math.Clamp(request.Limit * Math.Max(request.Page, 1), 1, 200),
                request.MinSimilarity,
                request.MaxMinLevel is { } ml ? new CefrFilter(ml) : null,
                ct: ct);

            var units = await lexicon.LoadUnitsAsync(matches.Select(m => m.LexicalUnitId).ToArray(), ct);
            var byId = units.ToDictionary(u => u.Id);
            var similarityById = matches.ToDictionary(m => m.LexicalUnitId, m => m.Similarity);

            var items = matches
                .Where(m => byId.ContainsKey(m.LexicalUnitId))
                .Select(m => byId[m.LexicalUnitId].ToResponse(similarityById[m.LexicalUnitId]))
                .ToArray();

            return Ok(new PagedResponse<LexicalUnitResponse>(items, request.Page, request.Limit, items.Length));
        }

        var result = await lexicon.SearchUnitsAsync(
            languageId,
            translationLanguageId,
            request.Query,
            request.MaxMinLevel,
            request.PartOfSpeech,
            request.Tags,
            request.Page,
            request.Limit,
            ct);

        return Ok(new PagedResponse<LexicalUnitResponse>(
            result.Items.Select(r => r.ToResponse()).ToArray(), result.Page, result.PageSize, result.Total));
    }

    /// <summary>
    /// Возвращает полную информацию о слове по его идентификатору.
    /// Включает перевод, транскрипцию, примеры, теги и другие метаданные слова.
    /// Используется для отображения детальной карточки слова.
    /// </summary>
    [HttpGet("words/{id}")]
    [ProducesResponseType(typeof(LexicalUnitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LexicalUnitResponse>> GetWord(Guid id, CancellationToken ct)
    {
        var units = await lexicon.LoadUnitsAsync([id], ct);
        var unit = units.FirstOrDefault();
        return unit is null ? NotFound() : Ok(unit.ToResponse());
    }

    /// <summary>
    /// Находит слова, близкие по смыслу к заданному слову.
    /// Использует векторные представления для поиска семантически похожих слов.
    /// Полезно для расширения словарного запаса — показывает синонимы и слова из той же тематической группы.
    /// </summary>
    [HttpGet("words/similar/{id}")]
    [ProducesResponseType(typeof(LexicalUnitResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<LexicalUnitResponse[]>> SimilarWords(Guid id, [FromQuery] int limit = 10, [FromQuery] string? provider = null, CancellationToken ct = default)
    {
        var matches = await vectorSearch.SearchByIdsAsync([id], provider, Math.Clamp(limit, 1, 50), 0.35, ct);
        var units = await lexicon.LoadUnitsAsync(matches.Select(m => m.LexicalUnitId).ToArray(), ct);
        var byId = units.ToDictionary(u => u.Id);
        var similarity = matches.ToDictionary(m => m.LexicalUnitId, m => m.Similarity);

        return Ok(matches.Where(m => byId.ContainsKey(m.LexicalUnitId))
            .Select(m => byId[m.LexicalUnitId].ToResponse(similarity[m.LexicalUnitId]))
            .ToArray());
    }

    /// <summary>
    /// Создаёт новое слово в словаре и автоматически добавляет его к пользователю.
    /// Проверяет на дубликаты — если такое слово уже есть, возвращает ошибку конфликта.
    /// При необходимости сразу вычисляет векторное представление для семантического поиска.
    /// </summary>
    [HttpPost("words")]
    [ProducesResponseType(typeof(LexicalUnitResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<LexicalUnitResponse>> CreateWord(CreateLexicalUnitRequest request, CancellationToken ct)
    {
        var user = await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageId = request.LanguageId ?? user.TargetLanguageId;
        var translationLanguageId = request.TranslationLanguageId ?? user.InterfaceLanguageId;
        var normalized = TextNormalizer.Normalize(request.Text);

        var existing = await lexicon.FindDuplicateAsync(languageId, translationLanguageId, normalized, ct);
        if (existing is not null)
        {
            return Conflict(new ErrorResponse("duplicate_word",
                "Такое слово уже есть в словаре.", new Dictionary<string, string[]> { ["text"] = [existing.Id.ToString()] }));
        }

        var unit = new LexicalUnit
        {
            LanguageId = languageId,
            TranslationLanguageId = translationLanguageId,
            Text = TextNormalizer.Collapse(request.Text),
            NormalizedText = normalized,
            Transcription = request.Transcription,
            Translation = request.Translation,
            AlternativeTranslations = request.AlternativeTranslations,
            PartOfSpeech = request.PartOfSpeech,
            Gender = request.Gender,
            PluralForm = request.PluralForm,
            PastTense = request.PastTense,
            AudioUrl = request.AudioUrl,
            ExampleTarget = request.ExampleTarget,
            ExampleNative = request.ExampleNative,
            Notes = request.Notes,
            Tags = request.Tags,
            MinLearnerLevel = request.MinLearnerLevel,
            FrequencyRank = request.FrequencyRank,
            Status = ContentStatus.Verified,
            AuthorUserId = user.Id
        };
        unit.ContentHash = TextNormalizer.ContentHash(unit.Text, unit.Translation, unit.ExampleTarget);

        await lexicon.CreateUnitAsync(unit, user.Id, request.DeckId, ContentSource.UserCreated, ct);

        if (request.ComputeEmbedding)
        {
            await TryEmbedAsync([unit.Id], ct);
        }

        // Перечитываем слово, чтобы в ответ попали данные, сохранённые базой.
        unit = (await lexicon.LoadUnitsAsync([unit.Id], ct)).First();
        return CreatedAtAction(nameof(GetWord), new { id = unit.Id }, unit.ToResponse());
    }

    /// <summary>
    /// Обновляет информацию о существующем слове в словаре.
    /// Обновляются только переданные поля; остальные данные остаются без изменений.
    /// Если изменился текст или перевод, автоматически пересчитывается векторное представление.
    /// </summary>
    [HttpPut("words/{id}")]
    [ProducesResponseType(typeof(LexicalUnitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LexicalUnitResponse>> UpdateWord(Guid id, UpdateLexicalUnitRequest request, CancellationToken ct)
    {
        var unit = await lexicon.UpdateUnitAsync(
            id,
            request.Text,
            request.Transcription,
            request.Translation,
            request.AlternativeTranslations,
            request.PartOfSpeech,
            request.Gender,
            request.PluralForm,
            request.PastTense,
            request.AudioUrl,
            request.ExampleTarget,
            request.ExampleNative,
            request.Notes,
            request.Tags,
            request.MinLearnerLevel,
            request.FrequencyRank,
            request.Status,
            ct);

        if (unit is null)
        {
            return NotFound();
        }

        if (request.ComputeEmbedding != false)
        {
            await TryEmbedAsync([unit.Id], ct);
        }

        return Ok(unit.ToResponse());
    }

    /// <summary>
    /// Удаляет (помечает как устаревшее) слово из словаря.
    /// Удаление мягкое — слово помечается как Deprecated и скрывается из поиска, но данные сохраняются.
    /// Удалить слово может только его автор или администратор/учитель.
    /// </summary>
    [HttpDelete("words/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWord(Guid id, CancellationToken ct)
    {
        var user = await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var unit = await lexicon.FindUnitAsync(id, ct);
        if (unit is null)
        {
            return NotFound();
        }

        if (unit.Status == ContentStatus.Verified && unit.AuthorUserId is not null && unit.AuthorUserId != user.Id
            && !IsAdmin(User))
        {
            return Forbid();
        }

        await lexicon.DeprecateUnitAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Массово импортирует слова из текста (например, в формате TSV — слово, перевод, транскрипция, пример).
    /// При импорте проверяются дубликаты: существующие слова обновляются, новые — добавляются.
    /// Возвращает подробный отчёт: сколько слов добавлено, обновлено, пропущено и сколько строк завершились ошибкой.
    /// </summary>
    [HttpPost("words/import")]
    [ProducesResponseType(typeof(ImportWordsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportWordsResponse>> Import(ImportWordsRequest request, CancellationToken ct)
    {
        var user = await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await lexicon.ImportUnitsAsync(
            new WordImportRequest(
                user.Id,
                request.LanguageId ?? user.TargetLanguageId,
                request.TranslationLanguageId ?? user.InterfaceLanguageId,
                request.Text,
                string.IsNullOrEmpty(request.Delimiter) ? "\t" : request.Delimiter,
                request.HasHeader,
                request.SkipDuplicates,
                request.MinLearnerLevel,
                request.DefaultTags,
                request.DeckId,
                _learning.MaxWordsPerImport),
            ct);

        if (request.ComputeEmbeddings)
        {
            await TryEmbedAsync(result.CreatedUnitIds, ct);
        }

        return Ok(new ImportWordsResponse(result.Imported, result.Updated, result.Skipped, result.Failed, result.Errors.ToArray()));
    }

    /// <summary>
    /// Возвращает слова, которые пользователь добавил в свой личный словарь.
    /// Можно фильтровать по состоянию карточки (новое, изучается, выучено) и по конкретной колоде.
    /// Используется для отображения персонального списка изучаемых слов.
    /// </summary>
    [HttpGet("my-words")]
    [ProducesResponseType(typeof(PagedResponse<UserWordResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<UserWordResponse>>> MyWords([FromQuery] PagedRequest request, [FromQuery] CardState? state, [FromQuery] Guid? deckId, CancellationToken ct)
    {
        var result = await lexicon.GetUserUnitsAsync(
            CurrentUserAccessor.GetUserId(User), state, deckId, request.Page, request.PageSize, ct);

        return Ok(new PagedResponse<UserWordResponse>(
            result.Items.Select(r => r.ToResponse()).ToArray(), result.Page, result.PageSize, result.Total));
    }

    /// <summary>
    /// Добавляет слово из общего словаря в личный словарь пользователя.
    /// Слово становится доступным для повторений и может быть добавлено в указанную колоду.
    /// Также можно сохранить личную заметку к слову.
    /// </summary>
    [HttpPost("my-words/{lexicalUnitId}")]
    [ProducesResponseType(typeof(UserWordResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<UserWordResponse>> AddMyWord(Guid lexicalUnitId, [FromQuery] Guid? deckId, [FromQuery] string? note, [FromQuery] ContentSource source = ContentSource.UserCreated, CancellationToken ct = default)
    {
        var entry = await lexicon.AddUserUnitAsync(CurrentUserAccessor.GetUserId(User), lexicalUnitId, deckId, source, note, ct);
        return entry is null
            ? NotFound()
            : StatusCode(StatusCodes.Status201Created, entry.ToResponse());
    }

    /// <summary>
    /// Удаляет слово из личного словаря пользователя.
    /// Слово перестаёт участвовать в повторениях и удаляется из всех колод пользователя.
    /// Само слово в общем словаре при этом не удаляется.
    /// </summary>
    [HttpDelete("my-words/{lexicalUnitId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMyWord(Guid lexicalUnitId, CancellationToken ct)
    {
        var removed = await lexicon.RemoveUserUnitAsync(CurrentUserAccessor.GetUserId(User), lexicalUnitId, ct);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Возвращает слово дня для текущего пользователя.
    /// Слово выбирается детерминированно на основе даты и идентификатора пользователя — у всех будет одинаковое слово в течение дня.
    /// Учитывает целевой язык и уровень владения пользователя.
    /// </summary>
    [HttpGet("word-of-the-day")]
    [ProducesResponseType(typeof(WordOfTheDayResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WordOfTheDayResponse>> WordOfTheDay(CancellationToken ct)
    {
        var user = await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var start = clock.UtcNow.Date;
        var seed = (int)(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeSeconds() / 86400) + user.Id.GetHashCode();

        var pool = await lexicon.GetWordPoolAsync(
            user.TargetLanguageId, user.InterfaceLanguageId, user.Level, WordOfTheDayPoolSize, ct);

        if (pool.Count == 0)
        {
            return NotFound(new ErrorResponse("empty_dictionary", "В словаре пока нет подходящих слов."));
        }

        var word = pool[Math.Abs(seed) % pool.Count];
        return Ok(new WordOfTheDayResponse(word.ToResponse(), $"Слово дня {start:dd.MM.yyyy}", word.AudioUrl));
    }

    /// <summary>
    /// Генерирует аудиофайл с произношением слова через сервис синтеза речи.
    /// Аудио сохраняется в базе данных и привязывается к слову, чтобы пользователь мог прослушать правильное произношение.
    /// Можно выбрать голос синтеза через параметр voice.
    /// </summary>
    [HttpPost("audio/word/{lexicalUnitId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateWordAudio(Guid lexicalUnitId, [FromQuery] string? voice, CancellationToken ct)
    {
        var user = await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var unit = await lexicon.FindUnitAsync(lexicalUnitId, ct);
        if (unit is null)
        {
            return NotFound();
        }

        var language = await catalog.FindLanguageAsync(unit.LanguageId, ct);
        if (language is null)
        {
            return NotFound();
        }

        var speech = HttpContext.RequestServices.GetRequiredService<ISpeechService>();
        var stored = await speech.SynthesizeAsync(unit.Text, language.Code, voice, user.SpeechRate, user.Id, ct);
        await lexicon.SetUnitAudioUrlAsync(unit.Id, stored.Url, ct);

        return Ok(new { audioUrl = stored.Url, mediaId = stored.Id });
    }

    /// <summary>Размер пула, из которого выбирается слово дня.</summary>
    private const int WordOfTheDayPoolSize = 200;

    private static bool IsAdmin(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.IsInRole("Admin") || principal.IsInRole("Teacher");

    /// <summary>
    /// Считает эмбеддинги «по возможности»: если ИИ-провайдер недоступен, слово всё равно остаётся в базе.
    /// </summary>
    private async Task TryEmbedAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        try
        {
            await embeddings.EnsureEmbeddingsAsync(ids, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Embeddings are best-effort: the word is stored even if the AI provider is unavailable.
        }
    }
}
