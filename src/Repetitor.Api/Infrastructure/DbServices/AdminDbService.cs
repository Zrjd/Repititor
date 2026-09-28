using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IAdminDbService"/> поверх фабрики контекстов EF Core.
/// </summary>
public sealed class AdminDbService(IDbContextFactory<AppDbContext> dbFactory) : IAdminDbService
{
    public async Task<PlatformStats> GetPlatformStatsAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return new PlatformStats(
            await db.Users.CountAsync(ct),
            await db.Users.CountAsync(u => u.LastLoginAt >= since, ct),
            await db.Languages.CountAsync(ct),
            await db.Courses.CountAsync(ct),
            await db.Lessons.CountAsync(ct),
            await db.LexicalUnits.CountAsync(u => u.Status != ContentStatus.Deprecated, ct),
            await db.LexicalUnitEmbeddings.CountAsync(ct),
            await db.Decks.CountAsync(ct),
            await db.ReviewCards.CountAsync(ct),
            await db.ReviewLogs.CountAsync(r => r.ReviewedAt >= since, ct),
            await db.Exercises.CountAsync(ct),
            await db.ExerciseAttempts.CountAsync(a => a.CompletedAt >= since, ct),
            await db.ChatSessions.CountAsync(ct),
            await db.AiCallLogs.CountAsync(l => l.CreatedAt >= since, ct),
            Math.Round(await db.AiCallLogs.Where(l => l.CreatedAt >= since).SumAsync(l => l.EstimatedCostUsd, ct), 6));
    }

    public async Task<DbPage<AdminUserItem>> GetUsersAsync(string? query, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Текст нормализуется, чтобы поиск не зависел от регистра и лишних пробелов.
            var normalized = TextNormalizer.Normalize(query);
            q = q.Where(u => u.Email.Contains(normalized) || u.DisplayName.ToLower().Contains(normalized));
        }

        var total = await q.CountAsync(ct);
        var safePage = Math.Max(page, 1);
        var rows = await q
            .OrderByDescending(u => u.CreatedAt)
            .Skip((safePage - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserItem(
                u.Id, u.Email, u.DisplayName, u.Role.ToString(), u.Level.ToString(),
                u.TotalXp, u.CurrentStreak, u.IsActive, u.EmailConfirmed, u.CreatedAt, u.LastLoginAt))
            .ToListAsync(ct);

        return new DbPage<AdminUserItem>(rows, safePage, pageSize, total);
    }

    public async Task<bool> SetUserRoleAsync(Guid userId, UserRole role, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return false;
        }

        user.Role = role;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetUserActiveAsync(Guid userId, bool active, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return false;
        }

        user.IsActive = active;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
