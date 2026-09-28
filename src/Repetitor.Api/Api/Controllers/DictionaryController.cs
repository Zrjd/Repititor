using System.Text;
using System.Text.Json;
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
[Route("api/v1/dictionary")]
[Authorize]
public sealed class DictionaryController(
    IDbContextFactory<AppDbContext> dbFactory,
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
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageId = request.LanguageId ?? user.TargetLanguageId;
        var translationLanguageId = request.TranslationLanguageId ?? user.InterfaceLanguageId;

        if (!string.IsNullOrWhiteSpace(request.Query) && request.Semantic)
        {
            var matches = await vectorSearch.SearchAsync(
                request.Query!, languageId, translationLanguageId, request.Provider,
                Math.Clamp(request.Limit * Math.Max(request.Page, 1), 1, 200),
                request.MinSimilarity,
                request.MaxMinLevel is { } ml ? new CefrFilter(ml) : null,
                ct: ct);

            var ids = matches.Select(m => m.LexicalUnitId).ToArray();
            var units = await dbFactory.LoadLexicalUnitsAsync(ids, ct);
            var byId = units.ToDictionary(u => u.Id);
            var similarityById = matches.ToDictionary(m => m.LexicalUnitId, m => m.Similarity);

            var items = matches
                .Where(m => byId.ContainsKey(m.LexicalUnitId))
                .Select(m => byId[m.LexicalUnitId].ToResponse(similarityById[m.LexicalUnitId]))
                .ToArray();

            return Ok(new PagedResponse<LexicalUnitResponse>(items, request.Page, request.Limit, items.Length));
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.LexicalUnits
            .Where(u => u.LanguageId == languageId && u.TranslationLanguageId == translationLanguageId)
            .Where(u => u.Status != ContentStatus.Deprecated);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var q = TextNormalizer.Normalize(request.Query!);
            var prefix = q.Length > 0 ? q[..Math.Min(q.Length, 3)] : q;
            query = query.Where(u =>
                EF.Functions.ILike(u.NormalizedText, prefix + "%") ||
                (u.Translation != null && EF.Functions.ILike(u.Translation, "%" + q + "%")));
        }

        if (request.MaxMinLevel is { } maxLevel)
        {
            query = query.Where(u => u.MinLearnerLevel <= maxLevel);
        }

        if (request.PartOfSpeech is { } pos)
        {
            query = query.Where(u => u.PartOfSpeech == pos);
        }

        if (request.Tags is { Length: > 0 } tags)
        {
            query = query.Where(u => u.Tags != null && tags.All(t => u.Tags!.Contains(t)));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(request.Page, 1);
        var rows = await query
            .OrderBy(u => u.FrequencyRank == 0 ? int.MaxValue : u.FrequencyRank)
            .ThenBy(u => u.Text)
            .Skip((page - 1) * request.Limit)
            .Take(request.Limit)
            .ToListAsync(ct);

        return Ok(new PagedResponse<LexicalUnitResponse>(
            rows.Select(r => r.ToResponse()).ToArray(), page, request.Limit, total));
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
        var units = await dbFactory.LoadLexicalUnitsAsync([id], ct);
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
        var ids = matches.Select(m => m.LexicalUnitId).ToArray();
        var units = await dbFactory.LoadLexicalUnitsAsync(ids, ct);
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
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageId = request.LanguageId ?? user.TargetLanguageId;
        var translationLanguageId = request.TranslationLanguageId ?? user.InterfaceLanguageId;
        var normalized = TextNormalizer.Normalize(request.Text);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.LexicalUnits.FirstOrDefaultAsync(
            u => u.LanguageId == languageId
                 && u.TranslationLanguageId == translationLanguageId
                 && u.NormalizedText == normalized, ct);

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

        db.LexicalUnits.Add(unit);
        await AttachToUserAsync(db, user.Id, unit, request.DeckId, ContentSource.UserCreated);
        await db.SaveChangesAsync(ct);

        if (request.ComputeEmbedding)
        {
            await TryEmbedAsync([unit.Id], ct);
        }

        unit = (await dbFactory.LoadLexicalUnitsAsync([unit.Id], ct)).First();
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return NotFound();
        }

        if (request.Text is not null)
        {
            unit.Text = TextNormalizer.Collapse(request.Text);
            unit.NormalizedText = TextNormalizer.Normalize(request.Text);
        }

        if (request.Transcription is not null) unit.Transcription = request.Transcription;
        if (request.Translation is not null) unit.Translation = request.Translation;
        if (request.AlternativeTranslations is not null) unit.AlternativeTranslations = request.AlternativeTranslations;
        if (request.PartOfSpeech is { } pos) unit.PartOfSpeech = pos;
        if (request.Gender is not null) unit.Gender = request.Gender;
        if (request.PluralForm is not null) unit.PluralForm = request.PluralForm;
        if (request.PastTense is not null) unit.PastTense = request.PastTense;
        if (request.AudioUrl is not null) unit.AudioUrl = request.AudioUrl;
        if (request.ExampleTarget is not null) unit.ExampleTarget = request.ExampleTarget;
        if (request.ExampleNative is not null) unit.ExampleNative = request.ExampleNative;
        if (request.Notes is not null) unit.Notes = request.Notes;
        if (request.Tags is not null) unit.Tags = request.Tags;
        if (request.MinLearnerLevel is { } lvl) unit.MinLearnerLevel = lvl;
        if (request.FrequencyRank is { } rank) unit.FrequencyRank = rank;
        if (request.Status is { } status) unit.Status = status;

        unit.ContentHash = TextNormalizer.ContentHash(unit.Text, unit.Translation, unit.ExampleTarget);
        await db.SaveChangesAsync(ct);

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
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return NotFound();
        }

        if (unit.Status == ContentStatus.Verified && unit.AuthorUserId is not null && unit.AuthorUserId != user.Id
            && !IsAdmin(User))
        {
            return Forbid();
        }

        unit.Status = ContentStatus.Deprecated;
        await db.SaveChangesAsync(ct);
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
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageId = request.LanguageId ?? user.TargetLanguageId;
        var translationLanguageId = request.TranslationLanguageId ?? user.InterfaceLanguageId;
        var delimiter = string.IsNullOrEmpty(request.Delimiter) ? "\t" : request.Delimiter;
        var errors = new List<string>();
        int imported = 0, updated = 0, skipped = 0, failed = 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lines = request.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var payload = new List<LexicalUnit>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (payload.Count >= _learning.MaxWordsPerImport)
            {
                errors.Add($"Импорт остановлен: достигнут лимит {_learning.MaxWordsPerImport} слов.");
                break;
            }

            var line = lines[i].TrimEnd('\r');
            if (request.HasHeader && i == 0)
            {
                continue;
            }

            var columns = line.Split(delimiter);
            var text = columns[0].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var normalized = TextNormalizer.Normalize(text);
            try
            {
                var existing = await db.LexicalUnits.FirstOrDefaultAsync(
                    u => u.LanguageId == languageId && u.TranslationLanguageId == translationLanguageId
                         && u.NormalizedText == normalized, ct);

                if (existing is not null)
                {
                    if (existing.Status == ContentStatus.Deprecated)
                    {
                        existing.Status = ContentStatus.Verified;
                        existing.Translation = columns.Length > 1 ? columns[1].Trim() : existing.Translation;
                        existing.ContentHash = TextNormalizer.ContentHash(existing.Text, existing.Translation, existing.ExampleTarget);
                        payload.Add(existing);
                        updated++;
                    }
                    else if (!request.SkipDuplicates)
                    {
                        if (columns.Length > 1 && !string.IsNullOrWhiteSpace(columns[1]))
                        {
                            existing.Translation = columns[1].Trim();
                            existing.ContentHash = TextNormalizer.ContentHash(existing.Text, existing.Translation, existing.ExampleTarget);
                            updated++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    else
                    {
                        skipped++;
                    }

                    continue;
                }

                var unit = new LexicalUnit
                {
                    LanguageId = languageId,
                    TranslationLanguageId = translationLanguageId,
                    Text = TextNormalizer.Collapse(text),
                    NormalizedText = normalized,
                    Translation = columns.Length > 1 ? columns[1].Trim() : null,
                    Transcription = columns.Length > 2 ? columns[2].Trim() : null,
                    ExampleTarget = columns.Length > 3 ? columns[3].Trim() : null,
                    MinLearnerLevel = request.MinLearnerLevel,
                    Tags = request.DefaultTags,
                    Status = ContentStatus.Verified,
                    AuthorUserId = user.Id
                };
                unit.ContentHash = TextNormalizer.ContentHash(unit.Text, unit.Translation, unit.ExampleTarget);
                db.LexicalUnits.Add(unit);
                payload.Add(unit);
                imported++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                errors.Add($"Строка {i + 1}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var unit in payload)
        {
            await AttachToUserAsync(db, user.Id, unit, request.DeckId, ContentSource.Imported);
        }

        await db.SaveChangesAsync(ct);

        if (request.ComputeEmbeddings && payload.Count > 0)
        {
            await TryEmbedAsync(payload.Select(p => p.Id).ToArray(), ct);
        }

        return Ok(new ImportWordsResponse(imported, updated, skipped, failed, errors.Take(20).ToArray()));
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
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.UserLexicalUnits
            .Include(u => u.LexicalUnit)
            .Where(u => u.UserId == userId);

        if (state is { } s)
        {
            query = query.Where(u => u.State == s);
        }

        if (deckId is { } did)
        {
            var ids = db.DeckCards.Where(dc => dc.DeckId == did).Select(dc => dc.UserLexicalUnitId);
            query = query.Where(u => ids.Contains(u.Id));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(request.Page, 1);
        var rows = await query
            .OrderBy(u => u.State)
            .ThenByDescending(u => u.AddedAt)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Ok(new PagedResponse<UserWordResponse>(rows.Select(r => r.ToResponse()).ToArray(), page, request.PageSize, total));
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
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == lexicalUnitId, ct);
        if (unit is null)
        {
            return NotFound();
        }

        var entry = await AttachToUserAsync(db, userId, unit, deckId, source);
        entry.PersonalNote = note;
        await db.SaveChangesAsync(ct);

        await db.Entry(entry).Reference(u => u.LexicalUnit).LoadAsync(ct);
        return StatusCode(StatusCodes.Status201Created, entry.ToResponse());
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
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.UserLexicalUnits
            .FirstOrDefaultAsync(u => u.UserId == userId && u.LexicalUnitId == lexicalUnitId, ct);

        if (entry is null)
        {
            return NotFound();
        }

        db.UserLexicalUnits.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
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
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var now = clock.UtcNow;
        var start = now.Date;
        var seed = (int)(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeSeconds() / 86400) + user.Id.GetHashCode();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pool = await db.LexicalUnits
            .Where(u => u.LanguageId == user.TargetLanguageId
                        && u.TranslationLanguageId == user.InterfaceLanguageId
                        && u.Status != ContentStatus.Deprecated
                        && u.MinLearnerLevel <= user.Level)
            .OrderBy(u => u.FrequencyRank)
            .Take(200)
            .ToListAsync(ct);

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
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateWordAudio(Guid lexicalUnitId, [FromQuery] string? voice, CancellationToken ct)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == lexicalUnitId, ct);
        if (unit is null)
        {
            return NotFound();
        }

        var speech = HttpContext.RequestServices.GetRequiredService<ISpeechService>();
        var language = await db.Languages.AsNoTracking().FirstAsync(l => l.Id == unit.LanguageId, ct);
        var stored = await speech.SynthesizeAsync(unit.Text, language.Code, voice, user.SpeechRate, user.Id, ct);

        unit.AudioUrl = stored.Url;
        await db.SaveChangesAsync(ct);

        return Ok(new { audioUrl = stored.Url, mediaId = stored.Id });
    }

    private static bool IsAdmin(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.IsInRole("Admin") || principal.IsInRole("Teacher");

    private async Task<User?> LoadUserAsync(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

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

    internal static async Task<UserLexicalUnit> AttachToUserAsync(
        AppDbContext db,
        Guid userId,
        LexicalUnit unit,
        Guid? deckId,
        ContentSource source)
    {
        var entry = await db.UserLexicalUnits
            .FirstOrDefaultAsync(u => u.UserId == userId && u.LexicalUnitId == unit.Id);

        if (entry is null)
        {
            entry = new UserLexicalUnit
            {
                UserId = userId,
                LexicalUnitId = unit.Id,
                State = CardState.New,
                Source = source
            };
            db.UserLexicalUnits.Add(entry);
        }
        else
        {
            entry.State = CardState.New;
            entry.Suspended = false;
        }

        var reviewCard = await db.ReviewCards.FirstOrDefaultAsync(r => r.UserLexicalUnitId == entry.Id);
        if (reviewCard is null)
        {
            reviewCard = new ReviewCard { UserLexicalUnitId = entry.Id, DueAt = DateTimeOffset.UtcNow };
            db.ReviewCards.Add(reviewCard);
        }
        else
        {
            reviewCard.SuspendedAt = null;
            reviewCard.DueAt = DateTimeOffset.UtcNow;
            reviewCard.State = CardState.New;
        }

        if (deckId is { } deck)
        {
            var deckEntity = await db.Decks.FirstOrDefaultAsync(d => d.Id == deck && d.UserId == userId);
            if (deckEntity is not null)
            {
                entry.LastDeckId = deckEntity.Id;
                var exists = await db.DeckCards.AnyAsync(dc => dc.DeckId == deckEntity.Id && dc.UserLexicalUnitId == entry.Id);
                if (!exists)
                {
                    var position = await db.DeckCards.CountAsync(dc => dc.DeckId == deckEntity.Id);
                    db.DeckCards.Add(new DeckCard
                    {
                        DeckId = deckEntity.Id,
                        UserLexicalUnitId = entry.Id,
                        Position = position
                    });
                }
            }
        }

        await db.SaveChangesAsync();
        return entry;
    }
}

internal static class DbFactoryLexicalExtensions
{
    public static async Task<List<LexicalUnit>> LoadLexicalUnitsAsync(
        this IDbContextFactory<AppDbContext> factory,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LexicalUnits.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
    }
}
