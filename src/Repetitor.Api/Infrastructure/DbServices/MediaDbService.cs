using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IMediaDbService"/> поверх фабрики контекстов EF Core.
/// </summary>
public sealed class MediaDbService(IDbContextFactory<AppDbContext> dbFactory) : IMediaDbService
{
    public async Task<IReadOnlyList<MediaAssetItem>> GetRecordingsAsync(Guid userId, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var assets = await db.MediaAssets
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new MediaAssetItem(
                a.Id,
                a.Kind.ToString(),
                a.ContentType,
                a.SizeBytes,
                a.DurationMs,
                a.SourceText,
                a.CreatedAt,
                a.ExpiresAt,
                "/media/" + a.StoragePath))
            .ToListAsync(ct);

        return assets;
    }

    public async Task<MediaAsset?> FindRecordingAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    }

    public async Task<bool> DeleteRecordingAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (asset is null)
        {
            return false;
        }

        db.MediaAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PronunciationAttempt> AddPronunciationAttemptAsync(PronunciationAttempt attempt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PronunciationAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task<IReadOnlyList<PronunciationAttempt>> GetPronunciationAttemptsAsync(Guid userId, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PronunciationAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
