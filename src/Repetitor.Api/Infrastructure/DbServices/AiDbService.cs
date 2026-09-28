using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IAiDbService"/> поверх фабрики контекстов EF Core.
/// Сводный отчёт считается в памяти по выбранным журналам: за период в несколько месяцев
/// строк немного, а группировка по провайдерам и дням нужна именно в таком виде.
/// </summary>
public sealed class AiDbService(IDbContextFactory<AppDbContext> dbFactory) : IAiDbService
{
    public async Task<AiUsageReport> GetUsageReportAsync(Guid? userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = db.AiCallLogs.Where(l => l.CreatedAt >= cutoff);
        if (userId is { } id)
        {
            query = query.Where(l => l.UserId == id);
        }

        var logs = await query.ToListAsync(ct);

        var byProvider = logs
            .GroupBy(l => l.Provider)
            .Select(g => new AiUsageByProviderItem(
                g.Key, g.Count(), g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens),
                Math.Round(g.Sum(x => x.EstimatedCostUsd), 6)))
            .ToArray();

        var byDay = logs
            .GroupBy(l => DateOnly.FromDateTime(l.CreatedAt.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new AiUsageByDayItem(g.Key, g.Count(), g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens)))
            .ToArray();

        return new AiUsageReport(
            from, to,
            logs.Count, logs.Count(l => !l.Success),
            logs.Sum(l => l.InputTokens), logs.Sum(l => l.OutputTokens),
            Math.Round(logs.Sum(l => l.EstimatedCostUsd), 6), byProvider, byDay);
    }

    public async Task PurgeLogsAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.AiCallLogs.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }

    public async Task<JsonObject?> GetSettingAsync(string key, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value as JsonObject;
    }

    public async Task SaveSettingAsync(string key, JsonObject value, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
