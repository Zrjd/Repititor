using System.Text.Json.Nodes;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к журналам вызовов ИИ и к системным настройкам в формате JSON.
/// Используется как для отчётов по расходу ресурсов, так и для хранения настроек провайдеров.
/// </summary>
public interface IAiDbService
{
    /// <summary>
    /// Возвращает сводный отчёт по использованию ИИ за период. Если userId не указан,
    /// учитываются вызовы всех пользователей — такой вариант доступен только администраторам.
    /// </summary>
    Task<AiUsageReport> GetUsageReportAsync(Guid? userId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Удаляет журналы вызовов ИИ, созданные раньше указанного момента.</summary>
    Task PurgeLogsAsync(DateTimeOffset cutoff, CancellationToken ct);

    /// <summary>Читает JSON-настройку по ключу. Возвращает null, если настройки ещё нет.</summary>
    Task<JsonObject?> GetSettingAsync(string key, CancellationToken ct);

    /// <summary>
    /// Создаёт или обновляет JSON-настройку. Значение передаётся уже готовым объектом,
    /// чтобы логика слияния с конфигурацией оставалась в контроллере.
    /// </summary>
    Task SaveSettingAsync(string key, JsonObject value, DateTimeOffset now, CancellationToken ct);
}
