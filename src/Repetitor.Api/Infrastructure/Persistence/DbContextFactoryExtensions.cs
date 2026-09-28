using Microsoft.EntityFrameworkCore;

namespace Repetitor.Api.Infrastructure.Persistence;

public static class DbContextFactoryExtensions
{
    /// <summary>
    /// Определяет код языка (например, "en", "ru") по его идентификатору GUID.
    /// Возвращает null, если идентификатор не указан или язык не найден. Используется для преобразования ID в читаемый код.
    /// </summary>
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

    /// <summary>
    /// Определяет коды языков для списка идентификаторов GUID за один запрос к БД.
    /// Возвращает словарь «GUID → код языка». Используется для массового преобразования ID в коды без множества отдельных запросов.
    /// </summary>
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
