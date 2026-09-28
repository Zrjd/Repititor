using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к медиафайлам пользователя и попыткам оценки произношения.
/// Физическое удаление файла остаётся на стороне хранилища, слой БД отвечает только за записи.
/// </summary>
public interface IMediaDbService
{
    /// <summary>Возвращает последние аудиозаписи пользователя с готовыми ссылками для выдачи.</summary>
    Task<IReadOnlyList<MediaAssetItem>> GetRecordingsAsync(Guid userId, int limit, CancellationToken ct);

    /// <summary>Находит запись пользователя, чтобы удалить файл из хранилища.</summary>
    Task<MediaAsset?> FindRecordingAsync(Guid userId, Guid id, CancellationToken ct);

    /// <summary>Удаляет запись о медиафайле. Возвращает false, если записи нет.</summary>
    Task<bool> DeleteRecordingAsync(Guid userId, Guid id, CancellationToken ct);

    /// <summary>Сохраняет попытку оценки произношения и возвращает её с присвоенным идентификатором.</summary>
    Task<PronunciationAttempt> AddPronunciationAttemptAsync(PronunciationAttempt attempt, CancellationToken ct);

    /// <summary>Возвращает последние попытки оценки произношения пользователя.</summary>
    Task<IReadOnlyList<PronunciationAttempt>> GetPronunciationAttemptsAsync(Guid userId, int limit, CancellationToken ct);
}
