using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/exercises")]
[Authorize]
public sealed class ExercisesController(
    IExerciseDbService exercises,
    IUserDbService users,
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
        var result = await exercises.ListAsync(
            CurrentUserAccessor.GetUserId(User), type, courseId, lessonId,
            mine, publishedOnly, Math.Max(request.Page, 1), request.PageSize, ct);

        return Ok(new PagedResponse<ExerciseSummaryResponse>(
            result.Items.Select(ToSummary).ToArray(), result.Page, result.PageSize, result.Total));
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
        var exercise = await exercises.FindAsync(id, ct);
        if (exercise is null)
        {
            return NotFound();
        }

        var stats = await exercises.GetUserStatsAsync(id, userId, ct);
        return Ok(ToResponse(exercise, includeAnswers, stats.Count, stats.BestScorePercent));
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

        var exercise = await exercises.FindNoTrackingAsync(report.Exercise.Id, ct)
            ?? throw new InvalidOperationException("Сгенерированное упражнение не найдено.");

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

        var result = await exercises.UpdateAsync(
            id,
            new ExerciseUpdate(
                request.Title, request.Instructions, request.Prompt, request.Payload,
                request.ExplanationMarkdown, request.Level, request.Points,
                request.EstimatedSeconds, request.IsPublished, request.IsActive),
            new DbActor(user.Id, user.Role),
            ct);

        return result.Status switch
        {
            ExerciseMutationStatus.NotFound => NotFound(),
            ExerciseMutationStatus.Forbidden => Forbid(),
            _ => Ok(ToResponse(result.Exercise!, true, 0, null))
        };
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
        var status = await exercises.DeactivateAsync(id, CurrentUserAccessor.GetUserId(User), ct);
        return status switch
        {
            ExerciseMutationStatus.NotFound => NotFound(),
            ExerciseMutationStatus.Forbidden => Forbid(),
            _ => NoContent()
        };
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
        var exercise = await exercises.FindAsync(id, ct);
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

        var saved = await exercises.RecordAttemptAsync(exercise, attempt, correct, ct);
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
        var attempts = await exercises.GetUserAttemptsAsync(
            id, CurrentUserAccessor.GetUserId(User), Math.Clamp(limit, 1, 100), ct);

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
        var attempts = await exercises.GetAttemptHistoryAsync(
            CurrentUserAccessor.GetUserId(User), Math.Clamp(limit, 1, 200), ct);

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

        var items = await exercises.GetRecommendedAsync(
            user.Id, user.Level, Math.Clamp(limit, 1, 20), ct);

        return Ok(items.Select(ToSummary).ToArray());
    }

    /// <summary>Собирает краткую карточку упражнения из записи, полученной от слоя доступа к данным.</summary>
    private static ExerciseSummaryResponse ToSummary(ExerciseListItem e) => new(
        e.Id, e.Type, e.Title, e.Instructions, e.Level, CountItems(e.Payload),
        e.Points, e.EstimatedSeconds, e.Source, e.AiProvider, e.AiModel,
        e.CourseId, e.LessonId, e.IsPublished, e.CreatedAt, e.BestScorePercent, e.Attempts);

    private async Task<User?> LoadUserAsync(CancellationToken ct) =>
        await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);

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
