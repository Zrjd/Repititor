using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к сводным данным для панели администратора: общая статистика платформы,
/// постраничный список пользователей и изменение их ролей и статусов.
/// </summary>
public interface IAdminDbService
{
    /// <summary>Собирает счётчики по всей платформе, где метрики за 7 дней считаются от указанного момента.</summary>
    Task<PlatformStats> GetPlatformStatsAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>Возвращает страницу пользователей с поиском по email и имени.</summary>
    Task<DbPage<AdminUserItem>> GetUsersAsync(string? query, int page, int pageSize, CancellationToken ct);

    /// <summary>Меняет роль пользователя. Возвращает false, если пользователь не найден.</summary>
    Task<bool> SetUserRoleAsync(Guid userId, UserRole role, CancellationToken ct);

    /// <summary>Активирует или деактивирует аккаунт пользователя. Возвращает false, если пользователь не найден.</summary>
    Task<bool> SetUserActiveAsync(Guid userId, bool active, CancellationToken ct);
}
