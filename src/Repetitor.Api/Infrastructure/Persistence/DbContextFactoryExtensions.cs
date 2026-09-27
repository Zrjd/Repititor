using Microsoft.EntityFrameworkCore;

namespace Repetitor.Api.Infrastructure.Persistence;

public static class DbContextFactoryExtensions
{
    public static async Task<string?> ResolveLanguageCodeAsync(
        this IDbContextFactory<AppDbContext> dbFactory,
        Guid? languageId,
        CancellationToken ct = default)
    {
        if (languageId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.Code)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<Dictionary<Guid, string>> ResolveLanguageCodesAsync(
        this IDbContextFactory<AppDbContext> dbFactory,
        IEnumerable<Guid> languageIds,
        CancellationToken ct = default)
    {
        var ids = languageIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.Code })
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
    }
}
