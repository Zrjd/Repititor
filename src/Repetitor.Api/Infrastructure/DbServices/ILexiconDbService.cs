using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к словарю, личному словарю пользователя, колодам и карточкам интервального повторения.
/// Содержит самый объёмный набор операций: поиск, импорт слов, прикрепление слов к пользователю,
/// управление колодами и построение прогноза повторений.
/// </summary>
public interface ILexiconDbService
{
    /// <summary>Загружает словарные единицы по списку идентификаторов. Пустой список даёт пустой результат.</summary>
    Task<IReadOnlyList<LexicalUnit>> LoadUnitsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Находит слово в общем словаре без отслеживания изменений.</summary>
    Task<LexicalUnit?> FindUnitAsync(Guid id, CancellationToken ct);

    /// <summary>Ищет уже существующее слово с тем же нормализованным текстом для пары языков.</summary>
    Task<LexicalUnit?> FindDuplicateAsync(Guid languageId, Guid translationLanguageId, string normalizedText, CancellationToken ct);

    /// <summary>Ищет слова по префиксу или вхождению с фильтрами по уровню, части речи и тегам.</summary>
    Task<DbPage<LexicalUnit>> SearchUnitsAsync(
        Guid languageId,
        Guid translationLanguageId,
        string? query,
        CefrLevel? maxMinLevel,
        PartOfSpeech? partOfSpeech,
        string[]? tags,
        int page,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Создаёт слово в словаре и сразу прикрепляет его к пользователю вместе с карточкой повторения.
    /// Возвращает уже сохранённое слово.
    /// </summary>
    Task<LexicalUnit> CreateUnitAsync(
        LexicalUnit unit,
        Guid userId,
        Guid? deckId,
        ContentSource source,
        CancellationToken ct);

    /// <summary>Частично обновляет слово и пересчитывает его хеш содержимого.</summary>
    Task<LexicalUnit?> UpdateUnitAsync(
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
        CancellationToken ct);

    /// <summary>Помечает слово как устаревшее — мягкое удаление, при котором данные остаются в базе.</summary>
    Task<LexicalUnit?> DeprecateUnitAsync(Guid id, CancellationToken ct);

    /// <summary>Сохраняет новый URL аудиофайла, сгенерированного для слова.</summary>
    Task SetUnitAudioUrlAsync(Guid id, string audioUrl, CancellationToken ct);

    /// <summary>Импортирует слова из текста, обновляя дубликаты и создавая новые записи.</summary>
    Task<WordImportResult> ImportUnitsAsync(WordImportRequest request, CancellationToken ct);

    /// <summary>Возвращает слова личного словаря пользователя с фильтрами по состоянию и колоде.</summary>
    Task<DbPage<UserLexicalUnit>> GetUserUnitsAsync(Guid userId, CardState? state, Guid? deckId, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Добавляет слово в личный словарь пользователя. Возвращает null, если самого слова нет в справочнике.
    /// Заодно создаёт или «будит» карточку повторения и при необходимости кладёт слово в колоду.
    /// </summary>
    Task<UserLexicalUnit?> AddUserUnitAsync(Guid userId, Guid lexicalUnitId, Guid? deckId, ContentSource source, string? note, CancellationToken ct);

    /// <summary>Удаляет слово из личного словаря пользователя. Возвращает false, если записи не было.</summary>
    Task<bool> RemoveUserUnitAsync(Guid userId, Guid lexicalUnitId, CancellationToken ct);

    /// <summary>Возвращает пул самых частотных слов, подходящих под язык и уровень пользователя.</summary>
    Task<IReadOnlyList<LexicalUnit>> GetWordPoolAsync(Guid languageId, Guid translationLanguageId, CefrLevel level, int take, CancellationToken ct);

    /// <summary>Возвращает колоды пользователя вместе с карточками и их карточками повторения.</summary>
    Task<IReadOnlyList<Deck>> GetDecksAsync(Guid userId, bool includeArchived, CancellationToken ct);

    /// <summary>Возвращает одну колоду пользователя вместе со всем содержимым.</summary>
    Task<Deck?> FindDeckAsync(Guid userId, Guid deckId, CancellationToken ct);

    /// <summary>Возвращает страницу карточек колоды в порядке добавления.</summary>
    Task<DbPage<UserLexicalUnit>?> GetDeckCardsAsync(Guid userId, Guid deckId, int page, int pageSize, CancellationToken ct);

    /// <summary>Создаёт колоду и, если переданы слова, сразу наполняет её карточками.</summary>
    Task<Deck> CreateDeckAsync(Deck deck, IReadOnlyCollection<Guid>? lexicalUnitIds, int maxDeckSize, CancellationToken ct);

    /// <summary>Частично обновляет колоду пользователя. Возвращает null, если колода не найдена.</summary>
    Task<Deck?> UpdateDeckAsync(
        Guid userId,
        Guid deckId,
        string? name,
        string? description,
        string? languageCode,
        string? coverEmoji,
        string[]? tags,
        bool? isArchived,
        CancellationToken ct);

    /// <summary>Удаляет колоду вместе со связями с карточками. Возвращает false, если колоды нет.</summary>
    Task<bool> DeleteDeckAsync(Guid userId, Guid deckId, CancellationToken ct);

    /// <summary>Добавляет слова в колоду, создавая недостающие записи личного словаря.</summary>
    Task<DeckCardsResult> AddCardsToDeckAsync(Guid userId, Guid deckId, IReadOnlyCollection<Guid> lexicalUnitIds, int maxDeckSize, CancellationToken ct);

    /// <summary>Убирает карточку из колоды, не удаляя само слово из личного словаря.</summary>
    Task<bool> RemoveDeckCardAsync(Guid userId, Guid deckId, Guid lexicalUnitId, CancellationToken ct);

    /// <summary>Сбрасывает прогресс изучения всех карточек колоды до начального состояния.</summary>
    Task ResetDeckProgressAsync(Guid deckId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Приостанавливает или возобновляет показ карточки в повторениях.</summary>
    Task<bool> SetCardSuspensionAsync(Guid userId, Guid reviewCardId, bool suspended, DateTimeOffset now, CancellationToken ct);

    /// <summary>Возвращает идентификаторы слов пользователя, добавленных последними — для предрасчёта эмбеддингов.</summary>
    Task<IReadOnlyList<Guid>> GetRecentUnitIdsAsync(Guid userId, int limit, CancellationToken ct);

    /// <summary>
    /// Строит прогноз нагрузки на повторения: сколько карточек будет готово к повторению
    /// на каждый из указанных дней, начиная с сегодняшнего.
    /// </summary>
    Task<IReadOnlyList<ForecastDayCount>> BuildForecastAsync(Guid userId, Guid? deckId, int days, DateTimeOffset now, CancellationToken ct);
}
