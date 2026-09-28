using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/exercises")]
[Authorize]
public sealed class ExercisesController(
    IDbContextFactory<AppDbContext> dbFactory,
    IExerciseGeneratorService generator,
    IAnswerGradingService grading,
    IProgressService progress,
    IEmbeddingService embeddings) : ControllerBase
{
    /// <summary>
    /// Возвращает список упражнений с фильтрацией и пагинацией.
    /// Позволяет фильтровать по типу, курсу, уроку, а также показывать только свои или только опубликованные упражнения.
    /// Для каждого упражнения включает статистику попыток текущего пользователя (количество и лучший результат).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ExerciseSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ExerciseSummaryResponse>>> List(
        [FromQuery] PagedRequest request,
        [FromQuery] ExerciseType? type,
        [FromQuery] Guid? courseId,
        [FromQuery] Guid? lessonId,
        [FromQuery] bool mine = false,
        [FromQuery] bool publishedOnly = false,
        CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
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
        var page = Math.Max(request.Page, 1);

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new
            {
                e.Id, e.Type, e.Title, e.Instructions, e.Level, e.Points, e.EstimatedSeconds,
                e.Source, e.AiProvider, e.AiModel, e.CourseId, e.LessonId, e.IsPublished, e.CreatedAt, e.Payload
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToArray();
        var attempts = await db.ExerciseAttempts
            .Where(a => a.UserId == userId && ids.Contains(a.ExerciseId))
            .GroupBy(a => a.ExerciseId)
            .Select(g => new { ExerciseId = g.Key, Count = g.Count(), Best = g.Max(a => a.ScorePercent) })
            .ToDictionaryAsync(x => x.ExerciseId, ct);

        var items = rows.Select(r => new ExerciseSummaryResponse(
            r.Id, r.Type.ToString(), r.Title, r.Instructions, r.Level.ToString(), CountItems(r.Payload),
            r.Points, r.EstimatedSeconds, r.Source.ToString(), r.AiProvider, r.AiModel,
            r.CourseId, r.LessonId, r.IsPublished, r.CreatedAt,
            attempts.GetValueOrDefault(r.Id) is { } a ? a.Best : null,
            attempts.GetValueOrDefault(r.Id)?.Count ?? 0)).ToArray();

        return Ok(new PagedResponse<ExerciseSummaryResponse>(items, page, request.PageSize, total));
    }

    /// <summary>
    /// Возвращает полную информацию об упражнении по его идентификатору.
    /// По умолчанию скрывает правильные ответы (includeAnswers = false), чтобы пользователь мог сначала выполнить упражнение.
    /// Также включает статистику попыток текущего пользователя по этому упражнению.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ExerciseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExerciseResponse>> Get(Guid id, [FromQuery] bool includeAnswers = false, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exercise is null)
        {
            return NotFound();
        }

        var stats = await db.ExerciseAttempts
            .Where(a => a.ExerciseId == id && a.UserId == userId)
            .GroupBy(a => a.UserId)
            .Select(g => new { Count = g.Count(), Best = g.Max(a => a.ScorePercent) })
            .FirstOrDefaultAsync(ct);

        return Ok(ToResponse(exercise, includeAnswers, stats?.Count ?? 0, stats?.Best));
    }

    /// <summary>
    /// Генерирует новое упражнение с помощью ИИ на основе заданных параметров.
    /// Можно указать контекст: конкретные слова, курс, урок или включить векторный контекст для автоматического подбора.
    /// Сгенерированное упражнение сохраняется в базу данных и возвращается с правильными ответами.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(ExerciseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExerciseResponse>> Generate(GenerateExerciseRequest request, CancellationToken ct)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var lexicalUnitIds = request.LexicalUnitIds ?? [];
        if (!request.UseVectorContext && lexicalUnitIds.Length == 0 && request.CourseId is null && request.LessonId is null)
        {
            return BadRequest(new ErrorResponse("no_context",
                "Укажите CourseId, LessonId или LexicalUnitIds, либо включите UseVectorContext."));
        }

        var report = await generator.GenerateAsync(new ExerciseGenerationRequest(
            request.Type,
            request.Level ?? user.Level,
            request.ItemCount,
            request.Topic,
            request.Provider ?? user.PreferredAiProvider,
            request.Model ?? user.PreferredChatModel,
            request.CourseId,
            request.LessonId,
            lexicalUnitIds,
            user.InterfaceLanguageId,
            Persist: true), ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.AsNoTracking().FirstAsync(e => e.Id == report.Exercise.Id, ct);

        if (lexicalUnitIds.Length > 0)
        {
            await embeddings.EnsureEmbeddingsAsync(lexicalUnitIds, null, ct);
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(exercise, true, 0, null));
    }

    /// <summary>
    /// Обновляет существующее упражнение.
    /// Изменить упражнение может только его автор или пользователь с правами администратора/учителя.
    /// Обновляются только те поля, которые переданы в запросе (остальные остаются без изменений).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ExerciseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExerciseResponse>> Update(Guid id, UpdateExerciseRequest request, CancellationToken ct)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exercise is null)
        {
            return NotFound();
        }

        if (exercise.OwnerUserId is not null && exercise.OwnerUserId != user.Id && user.Role == UserRole.Learner)
        {
            return Forbid();
        }

        if (request.Title is not null) exercise.Title = request.Title;
        if (request.Instructions is not null) exercise.Instructions = request.Instructions;
        if (request.Prompt is not null) exercise.Prompt = request.Prompt;
        if (request.Payload is not null) exercise.Payload = request.Payload;
        if (request.ExplanationMarkdown is not null) exercise.ExplanationMarkdown = request.ExplanationMarkdown;
        if (request.Level is { } level) exercise.Level = level;
        if (request.Points is { } points) exercise.Points = points;
        if (request.EstimatedSeconds is { } seconds) exercise.EstimatedSeconds = seconds;
        if (request.IsPublished is { } published) exercise.IsPublished = published;
        if (request.IsActive is { } active) exercise.IsActive = active;

        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(exercise, true, 0, null));
    }

    /// <summary>
    /// Удаляет (деактивирует) упражнение.
    /// Удаление мягкое — упражнение помечается как неактивное и исчезает из списков, но данные в базе сохраняются.
    /// Удалить может только автор упражнения; чужие упражнения удалять запрещено.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exercise is null)
        {
            return NotFound();
        }

        if (exercise.OwnerUserId is not null && exercise.OwnerUserId != userId)
        {
            return Forbid();
        }

        exercise.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Принимает ответы пользователя на упражнение, проверяет их и сохраняет результат попытки.
    /// Система оценивает правильность ответов, вычисляет процент успеха и начисляет XP.
    /// Также обновляет статистику использования упражнения (количество попыток, процент правильных ответов).
    /// </summary>
    [HttpPost("{id}/attempts")]
    [ProducesResponseType(typeof(ExerciseAttemptResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExerciseAttemptResponse>> Submit(Guid id, SubmitExerciseRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exercise is null)
        {
            return NotFound();
        }

        var answers = request.Answers?.DeepClone() as JsonNode ?? new JsonObject();
        if (request.FreeTextAnswer is not null)
        {
            answers["free_text"] = new JsonArray(JsonValue.Create(request.FreeTextAnswer));
        }

        var result = await grading.GradeExerciseAsync(exercise, answers, userId, ct);
        var total = result.Items.Count == 0 ? 1 : result.Items.Count;
        var correct = result.Items.Count(i => i["correct"]?.GetValue<bool>() == true);
        var xp = result.ScorePercent / 5;

        var attempt = new ExerciseAttempt
        {
            ExerciseId = id,
            UserId = userId,
            Answers = answers,
            CorrectCount = correct,
            TotalCount = total,
            ScorePercent = result.ScorePercent,
            IsPassed = result.IsCorrect,
            XpEarned = xp,
            AiFeedbackMarkdown = result.Explanation,
            AiProvider = exercise.AiProvider,
            AiModel = exercise.AiModel,
            DurationMs = Math.Clamp(request.DurationMs, 0, 86_400_000),
            StartedAt = DateTimeOffset.UtcNow.AddMilliseconds(-Math.Clamp(request.DurationMs, 0, 86_400_000)),
            CompletedAt = DateTimeOffset.UtcNow
        };
        db.ExerciseAttempts.Add(attempt);

        exercise.UsageCount++;
        exercise.LastUsedAt = DateTimeOffset.UtcNow;
        if (exercise.UsageCount > 0)
        {
            var rate = (exercise.CorrectRateBasisPoints * (exercise.UsageCount - 1) + correct * 10_000) / exercise.UsageCount;
            exercise.CorrectRateBasisPoints = rate;
        }

        await db.SaveChangesAsync(ct);
        await progress.RegisterActivityAsync(userId, xp, 0, correct, 0, 1, 0, request.DurationMs / 1000, ct);

        return Ok(new ExerciseAttemptResponse(
            attempt.Id, id, attempt.ScorePercent, attempt.IsPassed, correct, total, xp,
            attempt.AiFeedbackMarkdown,
            new JsonArray(result.Items.Select(i => (JsonNode)i.DeepClone()).ToArray()),
            attempt.DurationMs, attempt.CompletedAt));
    }

    /// <summary>
    /// Возвращает историю попыток текущего пользователя по конкретному упражнению.
    /// Показывает прошлые результаты в обратном хронологическом порядке, чтобы пользователь мог отследить свой прогресс.
    /// Количество записей можно ограничить параметром limit.
    /// </summary>
    [HttpGet("{id}/attempts")]
    [ProducesResponseType(typeof(ExerciseAttemptResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExerciseAttemptResponse[]>> Attempts(Guid id, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var attempts = await db.ExerciseAttempts
            .Where(a => a.ExerciseId == id && a.UserId == userId)
            .OrderByDescending(a => a.CompletedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Ok(attempts.Select(a => new ExerciseAttemptResponse(
            a.Id, a.ExerciseId, a.ScorePercent, a.IsPassed, a.CorrectCount, a.TotalCount, a.XpEarned,
            a.AiFeedbackMarkdown, null, a.DurationMs, a.CompletedAt)).ToArray());
    }

    /// <summary>
    /// Возвращает общую историю всех попыток текущего пользователя по всем упражнениям.
    /// Используется для страницы "Моя активность" или "История" в профиле пользователя.
    /// Записи сортируются по дате выполнения (новые сверху), количество ограничено параметром limit.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(ExerciseAttemptResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExerciseAttemptResponse[]>> History([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var attempts = await db.ExerciseAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CompletedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return Ok(attempts.Select(a => new ExerciseAttemptResponse(
            a.Id, a.ExerciseId, a.ScorePercent, a.IsPassed, a.CorrectCount, a.TotalCount, a.XpEarned,
            a.AiFeedbackMarkdown, null, a.DurationMs, a.CompletedAt)).ToArray());
    }

    /// <summary>
    /// Возвращает список рекомендованных упражнений для текущего пользователя.
    /// Рекомендации учитывают уровень владения пользователя и его прошлые результаты.
    /// Приоритет отдаётся упражнениям, которые пользователь ещё не пробовал или где результат был ниже 90%.
    /// </summary>
    [HttpGet("recommended")]
    [ProducesResponseType(typeof(ExerciseSummaryResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExerciseSummaryResponse[]>> Recommended([FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var userId = user.Id;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var exercises = await db.Exercises
            .Where(e => e.IsActive && e.IsPublished && e.Level <= user.Level)
            .OrderBy(e => e.UsageCount)
            .Take(Math.Clamp(limit, 1, 20) * 4)
            .ToListAsync(ct);

        var tried = await db.ExerciseAttempts
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.ExerciseId)
            .Select(g => new { ExerciseId = g.Key, Last = g.Max(a => a.CompletedAt), Best = g.Max(a => a.ScorePercent) })
            .ToDictionaryAsync(x => x.ExerciseId, ct);

        var selected = exercises
            .Where(e => !tried.TryGetValue(e.Id, out var t) || t.Best < 90)
            .OrderBy(e => tried.TryGetValue(e.Id, out var t) ? t.Last : DateTimeOffset.MinValue)
            .Take(Math.Clamp(limit, 1, 20))
            .ToList();

        return Ok(selected.Select(e => new ExerciseSummaryResponse(
            e.Id, e.Type.ToString(), e.Title, e.Instructions, e.Level.ToString(), CountItems(e.Payload),
            e.Points, e.EstimatedSeconds, e.Source.ToString(), e.AiProvider, e.AiModel,
            e.CourseId, e.LessonId, e.IsPublished, e.CreatedAt,
            tried.GetValueOrDefault(e.Id)?.Best, tried.GetValueOrDefault(e.Id) is null ? 0 : 1)).ToArray());
    }

    private async Task<User?> LoadUserAsync(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    private static ExerciseResponse ToResponse(Exercise e, bool includeAnswers, int attempts, int? best)
    {
        var payload = e.Payload?.DeepClone() as JsonNode;
        if (!includeAnswers)
        {
            payload = StripAnswers(payload);
        }

        return new ExerciseResponse(
            e.Id, e.Type.ToString(), e.Title, e.Instructions, e.Prompt, e.Level.ToString(),
            payload, e.ExplanationMarkdown, e.Topics, e.Points, e.EstimatedSeconds,
            e.Source.ToString(), e.AiProvider, e.AiModel, e.CourseId, e.LessonId, e.LanguageId,
            e.IsPublished, e.CreatedAt, best, attempts);
    }

    internal static JsonNode? StripAnswers(JsonNode? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var clone = payload.DeepClone();
        if (clone["items"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                item.Remove("correct_index");
                item.Remove("correct_answer");
                item.Remove("correct_translation");
                item.Remove("correct_sentence");
                item.Remove("explanation");
                item.Remove("accepted_variants");
            }
        }

        if (clone["pairs"] is JsonArray pairs)
        {
            foreach (var pair in pairs.OfType<JsonObject>())
            {
                pair.Remove("right");
            }
        }

        return clone;
    }

    private static int CountItems(JsonNode? payload)
    {
        if (payload is null)
        {
            return 0;
        }

        return payload["items"]?.AsArray().Count ?? payload["pairs"]?.AsArray().Count ?? 0;
    }
}
