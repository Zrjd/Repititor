using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IExerciseDbService"/> поверх фабрики контекстов EF Core.
/// Статистика попыток для списков загружается одним сгруппированным запросом,
/// чтобы не делать отдельный запрос на каждое упражнение.
/// </summary>
public sealed class ExerciseDbService(IDbContextFactory<AppDbContext> dbFactory) : IExerciseDbService
{
    public async Task<DbPage<ExerciseListItem>> ListAsync(
        Guid userId,
        ExerciseType? type,
        Guid? courseId,
        Guid? lessonId,
        bool mine,
        bool publishedOnly,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.Exercises.Where(e => e.IsActive);
        if (mine)
        {
            query = query.Where(e => e.OwnerUserId == userId);
        }
        else if (publishedOnly)
        {
            query = query.Where(e => e.IsPublished);
        }

        if (type is { } t)
        {
            query = query.Where(e => e.Type == t);
        }

        if (courseId is { } c)
        {
            query = query.Where(e => e.CourseId == c);
        }

        if (lessonId is { } l)
        {
            query = query.Where(e => e.LessonId == l);
        }

        var total = await query.CountAsync(ct);
        var safePage = Math.Max(page, 1);
        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((safePage - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.Type, e.Title, e.Instructions, e.Level, e.Points, e.EstimatedSeconds,
                e.Source, e.AiProvider, e.AiModel, e.CourseId, e.LessonId, e.IsPublished, e.CreatedAt, e.Payload
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToArray();
        var attempts = ids.Length == 0
            ? new Dictionary<Guid, (int Count, int Best)>()
            : await db.ExerciseAttempts
                .Where(a => a.UserId == userId && ids.Contains(a.ExerciseId))
                .GroupBy(a => a.ExerciseId)
                .Select(g => new { ExerciseId = g.Key, Count = g.Count(), Best = g.Max(a => a.ScorePercent) })
                .ToDictionaryAsync(x => x.ExerciseId, x => (x.Count, x.Best), ct);

        var items = rows.Select(r =>
        {
            var stat = attempts.GetValueOrDefault(r.Id);
            return new ExerciseListItem(
                r.Id, r.Type.ToString(), r.Title, r.Instructions, r.Level.ToString(), r.Payload,
                r.Points, r.EstimatedSeconds, r.Source.ToString(), r.AiProvider, r.AiModel,
                r.CourseId, r.LessonId, r.IsPublished, r.CreatedAt, stat.Best, stat.Count);
        }).ToArray();

        return new DbPage<ExerciseListItem>(items, safePage, pageSize, total);
    }

    public async Task<Exercise?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Exercise?> FindNoTrackingAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Exercises.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<ExerciseAttemptStats> GetUserStatsAsync(Guid exerciseId, Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stats = await db.ExerciseAttempts
            .Where(a => a.ExerciseId == exerciseId && a.UserId == userId)
            .GroupBy(a => a.UserId)
            .Select(g => new { Count = g.Count(), Best = g.Max(a => a.ScorePercent) })
            .FirstOrDefaultAsync(ct);

        return new ExerciseAttemptStats(stats?.Count ?? 0, stats?.Best);
    }

    public async Task<ExerciseMutationResult> UpdateAsync(Guid id, ExerciseUpdate update, DbActor actor, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exercise is null)
        {
            return new ExerciseMutationResult(ExerciseMutationStatus.NotFound, null);
        }

        // Право редактировать есть у владельца и у пользователей с ролью учителя или администратора.
        if (exercise.OwnerUserId is not null && exercise.OwnerUserId != actor.UserId && actor.Role == UserRole.Learner)
        {
            return new ExerciseMutationResult(ExerciseMutationStatus.Forbidden, null);
        }

        if (update.Title is not null) exercise.Title = update.Title;
        if (update.Instructions is not null) exercise.Instructions = update.Instructions;
        if (update.Prompt is not null) exercise.Prompt = update.Prompt;
        if (update.Payload is not null) exercise.Payload = update.Payload;
        if (update.ExplanationMarkdown is not null) exercise.ExplanationMarkdown = update.ExplanationMarkdown;
        if (update.Level is { } level) exercise.Level = level;
        if (update.Points is { } points) exercise.Points = points;
        if (update.EstimatedSeconds is { } seconds) exercise.EstimatedSeconds = seconds;
        if (update.IsPublished is { } published) exercise.IsPublished = published;
        if (update.IsActive is { } active) exercise.IsActive = active;

        await db.SaveChangesAsync(ct);
        return new ExerciseMutationResult(ExerciseMutationStatus.Ok, exercise);
    }

    public async Task<ExerciseMutationStatus> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exercise is null)
        {
            return ExerciseMutationStatus.NotFound;
        }

        // Удалять чужое упражнение нельзя, даже если у него нет владельца.
        if (exercise.OwnerUserId is not null && exercise.OwnerUserId != actorUserId)
        {
            return ExerciseMutationStatus.Forbidden;
        }

        exercise.IsActive = false;
        await db.SaveChangesAsync(ct);
        return ExerciseMutationStatus.Ok;
    }

    public async Task<ExerciseAttempt> RecordAttemptAsync(Exercise exercise, ExerciseAttempt attempt, int correctCount, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Exercises.FirstOrDefaultAsync(e => e.Id == exercise.Id, ct);
        if (tracked is null)
        {
            return attempt;
        }

        db.ExerciseAttempts.Add(attempt);

        tracked.UsageCount++;
        tracked.LastUsedAt = DateTimeOffset.UtcNow;
        if (tracked.UsageCount > 0)
        {
            // Средний процент верных ответов пересчитывается как взвешенная сумма по всем попыткам.
            tracked.CorrectRateBasisPoints = (tracked.CorrectRateBasisPoints * (tracked.UsageCount - 1) + correctCount * 10_000) / tracked.UsageCount;
        }

        await db.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task<IReadOnlyList<ExerciseAttempt>> GetUserAttemptsAsync(Guid exerciseId, Guid userId, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ExerciseAttempts
            .Where(a => a.ExerciseId == exerciseId && a.UserId == userId)
            .OrderByDescending(a => a.CompletedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExerciseAttempt>> GetAttemptHistoryAsync(Guid userId, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ExerciseAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CompletedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExerciseListItem>> GetRecommendedAsync(Guid userId, CefrLevel level, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var safeLimit = Math.Clamp(limit, 1, 20);

        var exercises = await db.Exercises
            .Where(e => e.IsActive && e.IsPublished && e.Level <= level)
            .OrderBy(e => e.UsageCount)
            .Take(safeLimit * 4)
            .ToListAsync(ct);

        var tried = await db.ExerciseAttempts
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.ExerciseId)
            .Select(g => new { ExerciseId = g.Key, Last = g.Max(a => a.CompletedAt), Best = g.Max(a => a.ScorePercent) })
            .ToDictionaryAsync(x => x.ExerciseId, ct);

        var selected = exercises
            .Where(e => !tried.TryGetValue(e.Id, out var t) || t.Best < 90)
            .OrderBy(e => tried.TryGetValue(e.Id, out var t) ? t.Last : DateTimeOffset.MinValue)
            .Take(safeLimit)
            .ToList();

        return selected.Select(e =>
        {
            var stat = tried.GetValueOrDefault(e.Id);
            return new ExerciseListItem(
                e.Id, e.Type.ToString(), e.Title, e.Instructions, e.Level.ToString(), e.Payload,
                e.Points, e.EstimatedSeconds, e.Source.ToString(), e.AiProvider, e.AiModel,
                e.CourseId, e.LessonId, e.IsPublished, e.CreatedAt,
                stat?.Best, stat is null ? 0 : 1);
        }).ToArray();
    }
}
