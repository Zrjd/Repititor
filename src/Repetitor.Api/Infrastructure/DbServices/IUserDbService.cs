using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к данным пользователей и их сессий. Скрывает от контроллеров любые обращения к БД,
/// связанные с учётными записями, профилем и токенами авторизации.
/// </summary>
public interface IUserDbService
{
    /// <summary>Ищет пользователя по email вместе с его целевым и интерфейсным языками.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>Ищет активного пользователя по email — используется при восстановлении пароля.</summary>
    Task<User?> FindActiveByEmailAsync(string email, CancellationToken ct);

    /// <summary>Ищет пользователя по идентификатору вместе с языками. Отслеживает изменения.</summary>
    Task<User?> FindWithLanguagesAsync(Guid userId, CancellationToken ct);

    /// <summary>Ищет пользователя по идентификатору без отслеживания изменений.</summary>
    Task<User?> FindAsync(Guid userId, CancellationToken ct);

    /// <summary>Проверяет, занят ли email в системе.</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);

    /// <summary>
    /// Создаёт пользователя вместе с его стартовой колодой «Мои слова» в одной транзакции.
    /// Возвращает сохранённого пользователя, которому уже присвоен идентификатор.
    /// </summary>
    Task<User> CreateWithFirstDeckAsync(
        User user,
        string firstDeckName,
        string firstDeckDescription,
        string languageCode,
        CancellationToken ct);

    /// <summary>
    /// Фиксирует успешный вход: обновляет время последнего входа и при необходимости
    /// перезаписывает хеш пароля, если алгоритм хеширования был обновлён.
    /// </summary>
    Task TouchLastLoginAsync(Guid userId, DateTimeOffset now, string? newPasswordHash, CancellationToken ct);

    /// <summary>
    /// Частично обновляет профиль. Перед сохранением проверяет существование языков:
    /// если язык не найден, изменения не применяются и возвращается имя некорректного поля.
    /// </summary>
    Task<UserProfileUpdateResult> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? avatarUrl,
        Guid? targetLanguageId,
        Guid? interfaceLanguageId,
        CefrLevel? level,
        CefrLevel? targetLevel,
        int? dailyGoalXp,
        double? speechRate,
        string? preferredAiProvider,
        string? preferredChatModel,
        bool? wantsCorrectionHints,
        bool? pushNotificationsEnabled,
        CancellationToken ct);

    /// <summary>Меняет хеш пароля пользователя.</summary>
    Task SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct);

    /// <summary>Находит активный refresh-токен вместе с пользователем и его языками.</summary>
    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken ct);

    /// <summary>Выпускает новый refresh-токен для указанного пользователя и устройства.</summary>
    Task<RefreshToken> AddRefreshTokenAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct);

    /// <summary>
    /// Выполняет ротацию refresh-токена: помечает старый отозванным и связывает его с новым.
    /// Обе операции сохраняются вместе, чтобы старый токен нельзя было использовать повторно.
    /// </summary>
    Task RotateRefreshTokenAsync(
        Guid currentTokenId,
        Guid userId,
        string newTokenHash,
        DateTimeOffset expiresAt,
        string? userAgent,
        string? ipAddress,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Отзывает один refresh-токен пользователя, если он ещё не был отозван.</summary>
    Task RevokeRefreshTokenAsync(Guid userId, string tokenHash, DateTimeOffset now, CancellationToken ct);

    /// <summary>Отзывает все активные refresh-токены пользователя — используется при выходе со всех устройств и смене пароля.</summary>
    Task RevokeAllRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Находит неиспользованный токен восстановления пароля.</summary>
    Task<PasswordResetToken?> FindPasswordResetTokenAsync(string tokenHash, CancellationToken ct);

    /// <summary>Создаёт токен восстановления пароля со сроком действия два часа.</summary>
    Task AddPasswordResetTokenAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>Помечает токен восстановления использованным, одновременно меняя пароль пользователя.</summary>
    Task CompletePasswordResetAsync(Guid tokenId, Guid userId, string passwordHash, DateTimeOffset now, CancellationToken ct);

    /// <summary>Собирает все показатели главной страницы пользователя в один объект.</summary>
    Task<UserDashboardSnapshot> GetDashboardAsync(Guid userId, DateTimeOffset now, int activityDays, CancellationToken ct);

    /// <summary>Возвращает ежедневную статистику пользователя начиная с указанной даты.</summary>
    Task<IReadOnlyList<DailyActivityItem>> GetDailyActivityAsync(Guid userId, DateOnly from, CancellationToken ct);

    /// <summary>Возвращает статистику пользователя за конкретный день — нужна для виджета дневной цели.</summary>
    Task<UserDailyStat?> GetDailyStatAsync(Guid userId, DateOnly date, CancellationToken ct);
}
