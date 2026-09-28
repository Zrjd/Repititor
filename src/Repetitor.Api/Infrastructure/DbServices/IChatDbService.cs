using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к чат-сессиям с ИИ-репетитором и их сообщениям.
/// Все операции проверяют принадлежность сессии владельцу, чтобы пользователь не мог
/// читать или изменять чужой диалог.
/// </summary>
public interface IChatDbService
{
    /// <summary>Возвращает последние сессии пользователя вместе с сообщениями.</summary>
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId, bool includeArchived, CancellationToken ct);

    /// <summary>Возвращает сессию пользователя вместе с сообщениями. Возвращает null, если сессии нет.</summary>
    Task<ChatSession?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);

    /// <summary>Загружает сессию вместе с сообщениями без проверки владельца — только что созданную.</summary>
    Task<ChatSession> LoadSessionAsync(Guid sessionId, CancellationToken ct);

    /// <summary>Проверяет, что сессия принадлежит пользователю.</summary>
    Task<bool> SessionExistsAsync(Guid userId, Guid sessionId, CancellationToken ct);

    /// <summary>Возвращает сообщения сессии, отсортированные по дате; используется пагинация «до даты».</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId, DateTimeOffset? before, int limit, CancellationToken ct);

    /// <summary>Архивирует или разархивирует сессию. Возвращает false, если сессии нет.</summary>
    Task<bool> SetArchivedAsync(Guid userId, Guid sessionId, bool archived, CancellationToken ct);

    /// <summary>Удаляет сессию вместе со всеми сообщениями. Возвращает false, если сессии нет.</summary>
    Task<bool> DeleteSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);

    /// <summary>Сохраняет оценку ответа репетитора. Возвращает false, если сообщение не найдено.</summary>
    Task<bool> SetMessageFeedbackAsync(Guid userId, Guid messageId, int? rating, CancellationToken ct);
}
