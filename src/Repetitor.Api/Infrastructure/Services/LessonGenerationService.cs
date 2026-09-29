using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.Services;

/// <summary>Параметры запроса генерации, которые сохраняются вместе с заданием.</summary>
public sealed record LessonGenerationRequest(
    string? Topic,
    CefrLevel? Level,
    string? Requirements,
    int? DurationMinutes,
    string? Summary,
    string? Provider,
    string? Model,
    string? LessonPrompt);

/// <summary>Текущее состояние генерации урока для интерфейса.</summary>
public sealed record LessonGenerationState(
    Guid LessonId,
    LessonGenerationStatus Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? CompletedAt,
    string? Error)
{
    public bool IsActive => Status is LessonGenerationStatus.Queued or LessonGenerationStatus.Running;
}

public enum LessonGenerationEnqueue
{
    Queued = 0,

    /// <summary>Генерация этого урока уже поставлена в очередь или выполняется.</summary>
    AlreadyActive = 1
}

public interface ILessonGenerationService
{
    Task<LessonGenerationEnqueue> EnqueueAsync(Guid lessonId, LessonGenerationRequest request, CancellationToken ct = default);

    Task<LessonGenerationState?> GetStateAsync(Guid lessonId, CancellationToken ct = default);

    /// <summary>Берёт следующее задание из очереди и выполняет его. Возвращает false, если очередь пуста.</summary>
    Task<bool> RunNextAsync(CancellationToken ct = default);

    /// <summary>Возвращает зависшие задания в очередь: после перезапуска и по таймауту.</summary>
    Task<int> RequeueStaleAsync(bool onStartup, CancellationToken ct = default);
}

/// <summary>
/// Фоновая генерация содержимого урока: запрос лишь ставит задание в очередь,
/// а воркер <see cref="LessonGenerationWorker"/> выполняет его и записывает результат в урок.
/// </summary>
public sealed class LessonGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    IContentGenerationService contentGeneration,
    IClock clock,
    IOptions<GenerationOptions> options,
    ILogger<LessonGenerationService> logger) : ILessonGenerationService
{
    private readonly GenerationOptions _options = options.Value;

    public async Task<LessonGenerationEnqueue> EnqueueAsync(
        Guid lessonId,
        LessonGenerationRequest request,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException("Lesson not found");

        if (lesson.AiGenerationStatus is LessonGenerationStatus.Queued or LessonGenerationStatus.Running)
        {
            return LessonGenerationEnqueue.AlreadyActive;
        }

        lesson.AiGenerationStatus = LessonGenerationStatus.Queued;
        lesson.AiGenerationRequestedAt = clock.UtcNow;
        lesson.AiGenerationCompletedAt = null;
        lesson.AiGenerationError = null;
        lesson.AiGenerationRequest = ToJson(request);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued AI generation for lesson {LessonId}", lessonId);
        return LessonGenerationEnqueue.Queued;
    }

    public async Task<LessonGenerationState?> GetStateAsync(Guid lessonId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Lessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new LessonGenerationState(l.Id, l.AiGenerationStatus, l.AiGenerationRequestedAt,
                l.AiGenerationCompletedAt, l.AiGenerationError))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> RunNextAsync(CancellationToken ct = default)
    {
        var lessonId = await ClaimNextAsync(ct);
        if (lessonId == Guid.Empty)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson?.AiGenerationRequest is null)
        {
            await MarkFailed(db, lesson, lessonId, "Задание потеряло параметры генерации", ct);
            return true;
        }

        try
        {
            var request = FromJson(lesson.AiGenerationRequest);
            var result = await contentGeneration.GenerateLessonAsync(
                lesson.CourseId,
                request.Topic,
                request.Level,
                request.Requirements,
                request.Provider,
                request.Model,
                request.LessonPrompt,
                request.DurationMinutes,
                request.Summary,
                ct);

            lesson.Title = AiText.Truncate(result.Title, 200);
            lesson.Summary = result.Summary is null ? null : AiText.Truncate(result.Summary, 1000);
            lesson.ContentMarkdown = result.ContentMarkdown;
            lesson.KeyVocabulary = result.KeyVocabulary;
            lesson.AiGenerationStatus = LessonGenerationStatus.Completed;
            lesson.AiGenerationCompletedAt = clock.UtcNow;
            lesson.AiGenerationError = null;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "AI generation for lesson {LessonId} completed: {Provider}/{Model}, tokens {In}/{Out}",
                lessonId, result.Provider, result.Model, result.InputTokens, result.OutputTokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI generation for lesson {LessonId} failed", lessonId);
            db.ChangeTracker.Clear();
            lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            await MarkFailed(db, lesson, lessonId, ex.Message, ct);
        }

        return true;
    }

    public async Task<int> RequeueStaleAsync(bool onStartup, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = clock.UtcNow.AddMinutes(-Math.Max(1, _options.StaleAfterMinutes));

        // После перезапуска в статусе Running ничего не выполняется, поэтому такие задания
        // возвращаются в очередь целиком; при обычном опросе — только те, что старше таймаута.
        var requeued = await db.Lessons
            .Where(l => l.AiGenerationStatus == LessonGenerationStatus.Running
                        && (onStartup || l.AiGenerationRequestedAt < cutoff))
            .ExecuteUpdateAsync(s =>
            {
                s.SetProperty(l => l.AiGenerationStatus, LessonGenerationStatus.Queued);
                s.SetProperty(l => l.AiGenerationError, (string?)null);
            }, ct);

        if (requeued > 0)
        {
            logger.LogWarning("Requeued {Count} interrupted AI generation job(s)", requeued);
        }

        return requeued;
    }

    private async Task<Guid> ClaimNextAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidate = await db.Lessons
            .Where(l => l.AiGenerationStatus == LessonGenerationStatus.Queued)
            .OrderBy(l => l.AiGenerationRequestedAt)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(ct);

        if (candidate == Guid.Empty)
        {
            return Guid.Empty;
        }

        // Условное обновление: если задание уже забрал другой воркер, строки не затронутся.
        var claimed = await db.Lessons
            .Where(l => l.Id == candidate && l.AiGenerationStatus == LessonGenerationStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.AiGenerationStatus, LessonGenerationStatus.Running), ct);

        return claimed == 1 ? candidate : Guid.Empty;
    }

    private async Task MarkFailed(AppDbContext db, Lesson? lesson, Guid lessonId, string message, CancellationToken ct)
    {
        if (lesson is null)
        {
            logger.LogWarning("Lesson {LessonId} disappeared while generating", lessonId);
            return;
        }

        lesson.AiGenerationStatus = LessonGenerationStatus.Failed;
        lesson.AiGenerationCompletedAt = clock.UtcNow;
        lesson.AiGenerationError = AiText.Truncate(message, _options.MaxErrorLength);
        await db.SaveChangesAsync(ct);
    }

    private static JsonObject ToJson(LessonGenerationRequest request) => LessonGenerationJson.Write(request);

    private static LessonGenerationRequest FromJson(JsonNode json) => LessonGenerationJson.Read(json);
}

/// <summary>
/// Формат сохранённого параметров задания. Вынесен отдельно, потому что параметры
/// переживают перезапуск и должны читаться без потерь.
/// </summary>
public static class LessonGenerationJson
{
    public static JsonObject Write(LessonGenerationRequest request) => new()
    {
        ["topic"] = request.Topic,
        ["requirements"] = request.Requirements,
        ["summary"] = request.Summary,
        ["provider"] = request.Provider,
        ["model"] = request.Model,
        ["lessonPrompt"] = request.LessonPrompt,
        ["level"] = request.Level?.ToString(),
        ["durationMinutes"] = request.DurationMinutes
    };

    public static LessonGenerationRequest Read(JsonNode json) => new(
        Text(json["topic"]),
        Enum.TryParse<CefrLevel>(Text(json["level"]), out var level) ? level : null,
        Text(json["requirements"]),
        Integer(json["durationMinutes"]),
        Text(json["summary"]),
        Text(json["provider"]),
        Text(json["model"]),
        Text(json["lessonPrompt"]));

    private static string? Text(JsonNode? node)
        => node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null ? null : node.GetValue<string>();

    private static int? Integer(JsonNode? node)
        => node is not null
           && node.GetValueKind() == System.Text.Json.JsonValueKind.Number
           && node.GetValue<int>() > 0
            ? node.GetValue<int>()
            : null;
}

/// <summary>
/// Периодически забирает задания из очереди и выполняет их по одному:
/// локальная модель справляется с одной генерацией за раз, а последовательный
/// цикл не позволяет запустить несколько тяжёлых запросов одновременно.
/// </summary>
public sealed class LessonGenerationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GenerationOptions> options,
    ILogger<LessonGenerationWorker> logger) : BackgroundService
{
    private readonly GenerationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            logger.LogInformation("AI lesson generation worker is disabled by configuration");
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 1, 300));
        logger.LogInformation("AI lesson generation worker started, poll interval {Interval}", poll);

        await RunSafelyAsync(() => RecoverAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            await RunSafelyAsync(async () =>
            {
                processed = await WithScopeAsync(s => s.RunNextAsync(stoppingToken), stoppingToken);
                if (processed)
                {
                    await WithScopeAsync(s => s.RequeueStaleAsync(false, stoppingToken), stoppingToken);
                }
            }, stoppingToken);

            // Пока очередь не кончилась, новые задания берём без паузы.
            if (processed)
            {
                continue;
            }

            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        var requeued = await WithScopeAsync(s => s.RequeueStaleAsync(true, ct), ct);
        if (requeued > 0)
        {
            logger.LogWarning("Returned {Count} interrupted job(s) to the queue on startup", requeued);
        }
    }

    private async Task<T> WithScopeAsync<T>(Func<ILessonGenerationService, Task<T>> action, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILessonGenerationService>());
    }

    private async Task RunSafelyAsync(Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Штатная остановка приложения.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI lesson generation worker iteration failed");
        }
    }
}
