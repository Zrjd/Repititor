using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к упражнениям и попыткам их выполнения. Проверка прав на изменение и удаление
/// выполняется внутри сервиса, чтобы запрещённая операция гарантированно ничего не сохраняла.
/// </summary>
public interface IExerciseDbService
{
    /// <summary>Возвращает страницу упражнений с фильтрами и статистикой попыток пользователя.</summary>
    Task<DbPage<ExerciseListItem>> ListAsync(
        Guid userId,
        ExerciseType? type,
        Guid? courseId,
        Guid? lessonId,
        bool mine,
        bool publishedOnly,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>Находит упражнение по идентификатору для чтения, оценки и сохранения попытки.</summary>
    Task<Exercise?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>Находит упражнение без отслеживания изменений — нужно сразу после генерации.</summary>
    Task<Exercise?> FindNoTrackingAsync(Guid id, CancellationToken ct);

    /// <summary>Возвращает количество и лучший результат попыток пользователя по упражнению.</summary>
    Task<ExerciseAttemptStats> GetUserStatsAsync(Guid exerciseId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Применяет частичное обновление упражнений. Возвращает статус, по которому контроллер
    /// понимает, отвечать 404, 403 или отдавать обновлённое упражнение.
    /// </summary>
    Task<ExerciseMutationResult> UpdateAsync(Guid id, ExerciseUpdate update, DbActor actor, CancellationToken ct);

    /// <summary>Мягко удаляет упражнение, если его владелец — переданный пользователь.</summary>
    Task<ExerciseMutationStatus> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken ct);

    /// <summary>
    /// Сохраняет попытку выполнения упражнения и пересчитывает накопленную статистику использования.
    /// Возвращает сохранённую попытку с присвоенным идентификатором и временем создания.
    /// </summary>
    Task<ExerciseAttempt> RecordAttemptAsync(Exercise exercise, ExerciseAttempt attempt, int correctCount, CancellationToken ct);

    /// <summary>Возвращает последние попытки пользователя по конкретному упражнению.</summary>
    Task<IReadOnlyList<ExerciseAttempt>> GetUserAttemptsAsync(Guid exerciseId, Guid userId, int limit, CancellationToken ct);

    /// <summary>Возвращает общую историю попыток пользователя по всем упражнениям.</summary>
    Task<IReadOnlyList<ExerciseAttempt>> GetAttemptHistoryAsync(Guid userId, int limit, CancellationToken ct);

    /// <summary>
    /// Подбирает рекомендуемые упражнения: сначала самые редко используемые, доступные по уровню,
    /// приоритет — тем, что ещё не решались или где лучший результат ниже 90 процентов.
    /// </summary>
    Task<IReadOnlyList<ExerciseListItem>> GetRecommendedAsync(Guid userId, CefrLevel level, int limit, CancellationToken ct);
}
