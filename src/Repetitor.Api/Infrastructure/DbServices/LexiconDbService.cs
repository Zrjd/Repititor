using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="ILexiconDbService"/> поверх фабрики контекстов EF Core.
/// Методы, меняющие несколько связанных сущностей (слово + карточка + колода), выполняются
/// в одном контексте, поэтому изменения попадают в базу атомарно.
/// </summary>
public sealed class LexiconDbService(IDbContextFactory<AppDbContext> dbFactory) : ILexiconDbService
{
    public async Task<IReadOnlyList<LexicalUnit>> LoadUnitsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LexicalUnits.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
    }

    public async Task<LexicalUnit?> FindUnitAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<LexicalUnit?> FindDuplicateAsync(
        Guid languageId,
        Guid translationLanguageId,
        string normalizedText,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LexicalUnits.FirstOrDefaultAsync(
            u => u.LanguageId == languageId
                 && u.TranslationLanguageId == translationLanguageId
                 && u.NormalizedText == normalizedText, ct);
    }

    public async Task<DbPage<LexicalUnit>> SearchUnitsAsync(
        Guid languageId,
        Guid translationLanguageId,
        string? query,
        CefrLevel? maxMinLevel,
        PartOfSpeech? partOfSpeech,
        string[]? tags,
        int page,
        int limit,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.LexicalUnits
            .Where(u => u.LanguageId == languageId && u.TranslationLanguageId == translationLanguageId)
            .Where(u => u.Status != ContentStatus.Deprecated);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = TextNormalizer.Normalize(query);
            var prefix = normalized.Length > 0 ? normalized[..Math.Min(normalized.Length, 3)] : normalized;
            q = q.Where(u =>
                EF.Functions.ILike(u.NormalizedText, prefix + "%") ||
                (u.Translation != null && EF.Functions.ILike(u.Translation, "%" + normalized + "%")));
        }

        if (maxMinLevel is { } maxLevel)
        {
            q = q.Where(u => u.MinLearnerLevel <= maxLevel);
        }

        if (partOfSpeech is { } pos)
        {
            q = q.Where(u => u.PartOfSpeech == pos);
        }

        if (tags is { Length: > 0 } tagList)
        {
            q = q.Where(u => u.Tags != null && tagList.All(t => u.Tags!.Contains(t)));
        }

        var total = await q.CountAsync(ct);
        var safePage = Math.Max(page, 1);
        var rows = await q
            .OrderBy(u => u.FrequencyRank == 0 ? int.MaxValue : u.FrequencyRank)
            .ThenBy(u => u.Text)
            .Skip((safePage - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new DbPage<LexicalUnit>(rows, safePage, limit, total);
    }

    public async Task<LexicalUnit> CreateUnitAsync(
        LexicalUnit unit,
        Guid userId,
        Guid? deckId,
        ContentSource source,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.LexicalUnits.Add(unit);
        await AttachToUserAsync(db, userId, unit, deckId, source);
        await db.SaveChangesAsync(ct);
        return unit;
    }

    public async Task<LexicalUnit?> UpdateUnitAsync(
        Guid id,
        string? text,
        string? transcription,
        string? translation,
        string[]? alternativeTranslations,
        PartOfSpeech? partOfSpeech,
        string? gender,
        string? pluralForm,
        string? pastTense,
        string? audioUrl,
        string? exampleTarget,
        string? exampleNative,
        string? notes,
        string[]? tags,
        CefrLevel? minLearnerLevel,
        int? frequencyRank,
        ContentStatus? status,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return null;
        }

        if (text is not null)
        {
            unit.Text = TextNormalizer.Collapse(text);
            unit.NormalizedText = TextNormalizer.Normalize(text);
        }

        if (transcription is not null) unit.Transcription = transcription;
        if (translation is not null) unit.Translation = translation;
        if (alternativeTranslations is not null) unit.AlternativeTranslations = alternativeTranslations;
        if (partOfSpeech is { } pos) unit.PartOfSpeech = pos;
        if (gender is not null) unit.Gender = gender;
        if (pluralForm is not null) unit.PluralForm = pluralForm;
        if (pastTense is not null) unit.PastTense = pastTense;
        if (audioUrl is not null) unit.AudioUrl = audioUrl;
        if (exampleTarget is not null) unit.ExampleTarget = exampleTarget;
        if (exampleNative is not null) unit.ExampleNative = exampleNative;
        if (notes is not null) unit.Notes = notes;
        if (tags is not null) unit.Tags = tags;
        if (minLearnerLevel is { } lvl) unit.MinLearnerLevel = lvl;
        if (frequencyRank is { } rank) unit.FrequencyRank = rank;
        if (status is { } newStatus) unit.Status = newStatus;

        unit.ContentHash = TextNormalizer.ContentHash(unit.Text, unit.Translation, unit.ExampleTarget);
        await db.SaveChangesAsync(ct);
        return unit;
    }

    public async Task<LexicalUnit?> DeprecateUnitAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return null;
        }

        unit.Status = ContentStatus.Deprecated;
        await db.SaveChangesAsync(ct);
        return unit;
    }

    public async Task SetUnitAudioUrlAsync(Guid id, string audioUrl, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return;
        }

        unit.AudioUrl = audioUrl;
        await db.SaveChangesAsync(ct);
    }

    public async Task<WordImportResult> ImportUnitsAsync(WordImportRequest request, CancellationToken ct)
    {
        var errors = new List<string>();
        int imported = 0, updated = 0, skipped = 0, failed = 0;
        var payload = new List<LexicalUnit>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lines = request.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            if (payload.Count >= request.MaxWords)
            {
                errors.Add($"Импорт остановлен: достигнут лимит {request.MaxWords} слов.");
                break;
            }

            var line = lines[i].TrimEnd('\r');
            if (request.HasHeader && i == 0)
            {
                continue;
            }

            var columns = line.Split(request.Delimiter);
            var text = columns[0].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var normalized = TextNormalizer.Normalize(text);
            try
            {
                var existing = await db.LexicalUnits.FirstOrDefaultAsync(
                    u => u.LanguageId == request.LanguageId
                         && u.TranslationLanguageId == request.TranslationLanguageId
                         && u.NormalizedText == normalized, ct);

                if (existing is not null)
                {
                    if (existing.Status == ContentStatus.Deprecated)
                    {
                        // Устаревшее слово «оживает»: возвращаем его в оборот и обновляем перевод.
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
                    LanguageId = request.LanguageId,
                    TranslationLanguageId = request.TranslationLanguageId,
                    Text = TextNormalizer.Collapse(text),
                    NormalizedText = normalized,
                    Translation = columns.Length > 1 ? columns[1].Trim() : null,
                    Transcription = columns.Length > 2 ? columns[2].Trim() : null,
                    ExampleTarget = columns.Length > 3 ? columns[3].Trim() : null,
                    MinLearnerLevel = request.MinLearnerLevel,
                    Tags = request.DefaultTags,
                    Status = ContentStatus.Verified,
                    AuthorUserId = request.UserId
                };
                unit.ContentHash = TextNormalizer.ContentHash(unit.Text, unit.Translation, unit.ExampleTarget);
                db.LexicalUnits.Add(unit);
                payload.Add(unit);
                imported++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Одна плохая строка не должна прерывать импорт: считаем ошибку и идём дальше.
                failed++;
                errors.Add($"Строка {i + 1}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var unit in payload)
        {
            await AttachToUserAsync(db, request.UserId, unit, request.DeckId, ContentSource.Imported);
        }

        await db.SaveChangesAsync(ct);
        return new WordImportResult(
            imported, updated, skipped, failed, errors.Take(20).ToArray(), payload.Select(u => u.Id).ToArray());
    }

    public async Task<DbPage<UserLexicalUnit>> GetUserUnitsAsync(
        Guid userId,
        CardState? state,
        Guid? deckId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
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
        var safePage = Math.Max(page, 1);
        var rows = await query
            .OrderBy(u => u.State)
            .ThenByDescending(u => u.AddedAt)
            .Skip((safePage - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new DbPage<UserLexicalUnit>(rows, safePage, pageSize, total);
    }

    public async Task<UserLexicalUnit?> AddUserUnitAsync(
        Guid userId,
        Guid lexicalUnitId,
        Guid? deckId,
        ContentSource source,
        string? note,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == lexicalUnitId, ct);
        if (unit is null)
        {
            return null;
        }

        var entry = await AttachToUserAsync(db, userId, unit, deckId, source);
        entry.PersonalNote = note;
        await db.SaveChangesAsync(ct);
        await db.Entry(entry).Reference(u => u.LexicalUnit).LoadAsync(ct);
        return entry;
    }

    public async Task<bool> RemoveUserUnitAsync(Guid userId, Guid lexicalUnitId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.UserLexicalUnits
            .FirstOrDefaultAsync(u => u.UserId == userId && u.LexicalUnitId == lexicalUnitId, ct);
        if (entry is null)
        {
            return false;
        }

        db.UserLexicalUnits.Remove(entry);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<LexicalUnit>> GetWordPoolAsync(
        Guid languageId,
        Guid translationLanguageId,
        CefrLevel level,
        int take,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LexicalUnits
            .Where(u => u.LanguageId == languageId
                        && u.TranslationLanguageId == translationLanguageId
                        && u.Status != ContentStatus.Deprecated
                        && u.MinLearnerLevel <= level)
            .OrderBy(u => u.FrequencyRank)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Deck>> GetDecksAsync(Guid userId, bool includeArchived, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Decks
            .Include(d => d.Cards).ThenInclude(c => c.UserLexicalUnit).ThenInclude(u => u!.ReviewCard)
            .Where(d => d.UserId == userId && (includeArchived || !d.IsArchived))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Deck?> FindDeckAsync(Guid userId, Guid deckId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Decks
            .Include(d => d.Cards).ThenInclude(c => c.UserLexicalUnit).ThenInclude(u => u!.ReviewCard)
            .FirstOrDefaultAsync(d => d.Id == deckId && d.UserId == userId, ct);
    }

    public async Task<DbPage<UserLexicalUnit>?> GetDeckCardsAsync(
        Guid userId,
        Guid deckId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Decks.AnyAsync(d => d.Id == deckId && d.UserId == userId, ct))
        {
            return null;
        }

        var query = db.DeckCards
            .Where(dc => dc.DeckId == deckId)
            .OrderBy(dc => dc.Position);

        var total = await query.CountAsync(ct);
        var safePage = Math.Max(page, 1);
        var rows = await query
            .Skip((safePage - 1) * pageSize)
            .Take(pageSize)
            .Select(dc => dc.UserLexicalUnit!)
            .ToListAsync(ct);

        return new DbPage<UserLexicalUnit>(rows, safePage, pageSize, total);
    }

    public async Task<Deck> CreateDeckAsync(
        Deck deck,
        IReadOnlyCollection<Guid>? lexicalUnitIds,
        int maxDeckSize,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Decks.Add(deck);
        await db.SaveChangesAsync(ct);

        if (lexicalUnitIds is { Count: > 0 })
        {
            await AddCardsInternalAsync(db, deck.UserId, deck.Id, lexicalUnitIds, maxDeckSize, ct);
        }

        return deck;
    }

    public async Task<Deck?> UpdateDeckAsync(
        Guid userId,
        Guid deckId,
        string? name,
        string? description,
        string? languageCode,
        string? coverEmoji,
        string[]? tags,
        bool? isArchived,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == deckId && d.UserId == userId, ct);
        if (deck is null)
        {
            return null;
        }

        if (name is not null) deck.Name = TextNormalizer.Collapse(name);
        if (description is not null) deck.Description = description;
        if (languageCode is not null) deck.LanguageCode = languageCode;
        if (coverEmoji is not null) deck.CoverEmoji = coverEmoji;
        if (tags is not null) deck.Tags = tags;
        if (isArchived is { } archived) deck.IsArchived = archived;

        await db.SaveChangesAsync(ct);
        return deck;
    }

    public async Task<bool> DeleteDeckAsync(Guid userId, Guid deckId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == deckId && d.UserId == userId, ct);
        if (deck is null)
        {
            return false;
        }

        db.Decks.Remove(deck);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<DeckCardsResult> AddCardsToDeckAsync(
        Guid userId,
        Guid deckId,
        IReadOnlyCollection<Guid> lexicalUnitIds,
        int maxDeckSize,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Decks.AnyAsync(d => d.Id == deckId && d.UserId == userId, ct))
        {
            return new DeckCardsResult(false, 0, 0);
        }

        var added = await AddCardsInternalAsync(db, userId, deckId, lexicalUnitIds, maxDeckSize, ct);
        var size = await db.DeckCards.CountAsync(dc => dc.DeckId == deckId, ct);
        return new DeckCardsResult(true, added, size);
    }

    public async Task<bool> RemoveDeckCardAsync(Guid userId, Guid deckId, Guid lexicalUnitId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var card = await db.DeckCards
            .Where(dc => dc.DeckId == deckId
                         && dc.UserLexicalUnit!.LexicalUnitId == lexicalUnitId
                         && dc.Deck!.UserId == userId)
            .FirstOrDefaultAsync(ct);
        if (card is null)
        {
            return false;
        }

        db.DeckCards.Remove(card);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ResetDeckProgressAsync(Guid deckId, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var unitIds = db.DeckCards.Where(dc => dc.DeckId == deckId).Select(dc => dc.UserLexicalUnitId);
        var cards = await db.ReviewCards.Where(rc => unitIds.Contains(rc.UserLexicalUnitId)).ToListAsync(ct);
        var entries = await db.UserLexicalUnits.Where(u => unitIds.Contains(u.Id)).ToListAsync(ct);

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

        foreach (var entry in entries)
        {
            entry.State = CardState.New;
            entry.MasteryScore = 0;
            entry.CorrectStreak = 0;
            entry.IncorrectStreak = 0;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> SetCardSuspensionAsync(Guid userId, Guid reviewCardId, bool suspended, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var card = await db.ReviewCards
            .FirstOrDefaultAsync(c => c.Id == reviewCardId && c.UserLexicalUnit!.UserId == userId, ct);
        if (card is null)
        {
            return false;
        }

        card.SuspendedAt = suspended ? now : null;
        card.UserLexicalUnit!.Suspended = suspended;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> GetRecentUnitIdsAsync(Guid userId, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserLexicalUnits
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.AddedAt)
            .Select(u => u.LexicalUnitId)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ForecastDayCount>> BuildForecastAsync(
        Guid userId,
        Guid? deckId,
        int days,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = from card in db.ReviewCards
            join unit in db.UserLexicalUnits on card.UserLexicalUnitId equals unit.Id
            where unit.UserId == userId && card.SuspendedAt == null
            select card;

        if (deckId is { } deck)
        {
            var ids = db.DeckCards.Where(dc => dc.DeckId == deck).Select(dc => dc.UserLexicalUnitId);
            query = query.Where(c => ids.Contains(c.UserLexicalUnitId));
        }

        var cards = await query.ToListAsync(ct);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var forecast = new List<ForecastDayCount>(days);
        for (var i = 0; i < days; i++)
        {
            var date = today.AddDays(i);
            forecast.Add(new ForecastDayCount(date, cards.Count(c => DateOnly.FromDateTime(c.DueAt.UtcDateTime) <= date)));
        }

        return forecast;
    }

    /// <summary>
    /// Прикрепляет слово к пользователю: создаёт запись личного словаря и карточку повторения,
    /// при необходимости добавляет карточку в колоду. Использует переданный контекст,
    /// чтобы вызывающая сторона могла сохранить всё одной транзакцией.
    /// </summary>
    private static async Task<UserLexicalUnit> AttachToUserAsync(
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
            // Повторное добавление возвращает слово в активные: снимаем паузу и сбрасываем состояние.
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

    private static async Task<int> AddCardsInternalAsync(
        AppDbContext db,
        Guid userId,
        Guid deckId,
        IReadOnlyCollection<Guid> lexicalUnitIds,
        int maxDeckSize,
        CancellationToken ct)
    {
        var deckSize = await db.DeckCards.CountAsync(dc => dc.DeckId == deckId, ct);
        if (deckSize + lexicalUnitIds.Count > maxDeckSize)
        {
            throw new InvalidOperationException($"Достигнут лимит колоды: {maxDeckSize} карточек.");
        }

        var added = 0;
        foreach (var lexicalUnitId in lexicalUnitIds.Distinct())
        {
            var unit = await db.LexicalUnits.FirstOrDefaultAsync(u => u.Id == lexicalUnitId, ct);
            if (unit is null)
            {
                continue;
            }

            var entry = await AttachToUserAsync(db, userId, unit, deckId, ContentSource.UserCreated);
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
