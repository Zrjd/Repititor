using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IChatDbService"/> поверх фабрики контекстов EF Core.
/// </summary>
public sealed class ChatDbService(IDbContextFactory<AppDbContext> dbFactory) : IChatDbService
{
    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId, bool includeArchived, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.UserId == userId && (includeArchived || !s.IsArchived))
            .OrderByDescending(s => s.LastMessageAt)
            .Take(100)
            .ToListAsync(ct);
    }

    public async Task<ChatSession?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
    }

    public async Task<ChatSession> LoadSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChatSessions
            .Include(s => s.Messages)
            .AsNoTracking()
            .FirstAsync(s => s.Id == sessionId, ct);
    }

    public async Task<bool> SessionExistsAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChatSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId, DateTimeOffset? before, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.ChatMessages.Where(m => m.SessionId == sessionId);
        if (before is { } from)
        {
            query = query.Where(m => m.CreatedAt < from);
        }

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> SetArchivedAsync(Guid userId, Guid sessionId, bool archived, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return false;
        }

        session.IsArchived = archived;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return false;
        }

        db.ChatSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetMessageFeedbackAsync(Guid userId, Guid messageId, int? rating, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.Session!.UserId == userId, ct);
        if (message is null)
        {
            return false;
        }

        message.FeedbackRating = rating;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
