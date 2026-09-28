using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/ai")]
public sealed class AiController(
    IAiGateway gateway,
    IAiDbService aiDb,
    IEmbeddingService embeddings,
    IClock clock,
    IOptions<AiOptions> aiOptions,
    IOptions<LearningOptions> learningOptions) : ControllerBase
{
    /// <summary>
    /// Возвращает список всех сконфигурированных провайдеров ИИ с их настройками.
    /// Позволяет клиенту отображать доступные модели и их возможности (стриминг, JSON-режим, измерения эмбеддингов).
    /// Доступен без аутентификации, так как информация не содержит секретных данных.
    /// </summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AiProviderResponse[]), StatusCodes.Status200OK)]
    public ActionResult<AiProviderResponse[]> Providers()
    {
        var result = gateway.DescribeProviders()
            .Select(p => new AiProviderResponse(
                p.Name, p.Kind.ToString(), p.Enabled, p.Configured, p.ChatModel, p.EmbeddingModel,
                p.TtsModel, p.SttModel, p.SupportsStreaming, p.SupportsJsonMode, p.EmbeddingDimensions,
                p.RequestsPerMinute))
            .ToArray();

        return Ok(result);
    }

    /// <summary>
    /// Проверяет работоспособность указанного провайдера ИИ, отправляя тестовый запрос.
    /// Измеряет задержку ответа и возвращает используемую модель.
    /// При ошибке возвращает 503 с описанием проблемы, что позволяет мониторингу отслеживать состояние сервисов.
    /// </summary>
    [HttpPost("providers/{name}/health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Health(string name, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = gateway.ResolveChat(name);
            var result = await client.CompleteAsync(new AiChatRequest
            {
                Messages = [AiChatMessage.User("ping")],
                MaxTokens = 8,
                Temperature = 0
            }, ct);

            return Ok(new
            {
                provider = name,
                healthy = true,
                model = result.Model,
                latencyMs = sw.ElapsedMilliseconds,
                checkedAt = clock.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                provider = name,
                healthy = false,
                error = ex.Message,
                checkedAt = clock.UtcNow
            });
        }
    }

    /// <summary>
    /// Возвращает статистику использования ИИ за указанный период (по умолчанию 30 дней).
    /// Администраторы и преподаватели могут просматривать статистику всех пользователей,
    /// остальные пользователи видят только свои данные. Включает разбивку по провайдерам и дням.
    /// </summary>
    [HttpGet("usage")]
    [Authorize]
    [ProducesResponseType(typeof(AiUsageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiUsageResponse>> Usage([FromQuery] int days = 30, [FromQuery] bool allUsers = false, CancellationToken ct = default)
    {
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Teacher");
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = today.AddDays(-Math.Clamp(days, 1, 365) + 1);

        // Статистику по всем пользователям видят только администраторы и преподаватели.
        var report = await aiDb.GetUsageReportAsync(
            allUsers && isAdmin ? null : CurrentUserAccessor.GetUserId(User), from, today, ct);

        return Ok(new AiUsageResponse(
            report.From, report.To,
            report.TotalCalls, report.FailedCalls,
            report.InputTokens, report.OutputTokens, report.EstimatedCostUsd,
            report.ByProvider.Select(p => new AiUsageByProviderResponse(
                p.Provider, p.Calls, p.InputTokens, p.OutputTokens, p.EstimatedCostUsd)).ToArray(),
            report.ByDay.Select(d => new AiUsageByDayResponse(d.Date, d.Calls, d.InputTokens, d.OutputTokens)).ToArray()));
    }

    /// <summary>
    /// Пересоздаёт индекс эмбеддингов словаря для полнотекстового и семантического поиска.
    /// Позволяет указать провайдера, размер пакета и ограничение на количество записей.
    /// Параметр force принудительно пересоздаёт существующие эмбеддинги.
    /// Операция глобальная и расходует квоту ИИ, поэтому доступна только администратору.
    /// </summary>
    [HttpPost("dictionary/reindex")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ReindexResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReindexResponse>> Reindex([FromQuery] string? provider, [FromQuery] int batchSize = 64, [FromQuery] int limit = 1000, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var report = await embeddings.ReindexAsync(provider, batchSize, limit, force, ct);
        return Ok(new ReindexResponse(report.Processed, report.Created, report.Failed));
    }

    /// <summary>
    /// Возвращает текущие ограничения системы: лимиты на импорт слов, размер колоды,
    /// количество бесплатных вызовов ИИ в день, параметры повторений и поддерживаемые типы упражнений.
    /// Используется клиентом для отображения актуальных ограничений и настройки пользовательского интерфейса.
    /// </summary>
    [HttpGet("limits")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> Limits()
    {
        var options = learningOptions.Value;
        return Ok(new
        {
            maxWordsPerImport = options.MaxWordsPerImport,
            maxDeckSize = options.MaxDeckSize,
            maxFreeAiCallsPerDay = options.MaxFreeAiCallsPerDay,
            newCardsPerDay = options.NewCardsPerDay,
            maxOutputTokens = aiOptions.Value.MaxOutputTokens,
            embeddingDimensions = aiOptions.Value.EmbeddingDimensions,
            embeddingLocalDimensions = aiOptions.Value.EmbeddingLocalDimensions,
            supportedExerciseTypes = new[]
            {
                ExerciseType.MultipleChoice, ExerciseType.TranslateToTarget, ExerciseType.TranslateFromTarget,
                ExerciseType.GapFill, ExerciseType.WordOrder, ExerciseType.MatchPairs,
                ExerciseType.Listening, ExerciseType.Writing
            }
        });
    }
}

/// <summary>
/// Платформенные административные операции: статистика, управление пользователями и очистка журналов ИИ.
/// Доступно только администратору: роль учителя ограничена собственными курсами и уроками.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminController(
    IAdminDbService admin,
    IAiDbService aiDb,
    IClock clock) : ControllerBase
{
    /// <summary>
    /// Возвращает агрегированную статистику платформы для панели администратора.
    /// Включает количество пользователей, активных за 7 дней, контент (курсы, уроки, слова),
    /// статистику повторений, упражнений и использования ИИ с оценкой стоимости.
    /// </summary>
    /// <summary>
    /// Возвращает агрегированную статистику платформы для панели администратора.
    /// Включает количество пользователей, активных за 7 дней, контент (курсы, уроки, слова),
    /// статистику повторений, упражнений и использования ИИ с оценкой стоимости.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var stats = await admin.GetPlatformStatsAsync(clock.UtcNow.AddDays(-7), ct);

        return Ok(new
        {
            users = stats.Users,
            activeUsers7d = stats.ActiveUsers7d,
            languages = stats.Languages,
            courses = stats.Courses,
            lessons = stats.Lessons,
            words = stats.Words,
            wordsWithEmbeddings = stats.WordsWithEmbeddings,
            decks = stats.Decks,
            reviewCards = stats.ReviewCards,
            reviews7d = stats.Reviews7d,
            exercises = stats.Exercises,
            attempts7d = stats.Attempts7d,
            chatSessions = stats.ChatSessions,
            aiCalls7d = stats.AiCalls7d,
            aiCost7dUsd = stats.AiCost7dUsd
        });
    }

    /// <summary>
    /// Возвращает постраничный список пользователей с возможностью поиска по email или имени.
    /// Поиск нормализует введённый текст для регистронезависимого сравнения.
    /// Результат включает основные профильные данные: роль, уровень, XP, серию дней и статус аккаунта.
    /// </summary>
    /// <summary>
    /// Возвращает постраничный список пользователей с возможностью поиска по email или имени.
    /// Поиск нормализует введённый текст для регистронезависимого сравнения.
    /// Результат включает основные профильные данные: роль, уровень, XP, серию дней и статус аккаунта.
    /// </summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<object>>> Users([FromQuery] PagedRequest request, [FromQuery] string? query, CancellationToken ct)
    {
        var result = await admin.GetUsersAsync(query, Math.Max(request.Page, 1), request.PageSize, ct);

        return Ok(new PagedResponse<object>(
            result.Items
                .Select(u => (object)new
                {
                    u.Id, u.Email, u.DisplayName, u.Role, u.Level,
                    u.TotalXp, u.CurrentStreak, u.IsActive, u.EmailConfirmed, u.CreatedAt, u.LastLoginAt
                })
                .ToArray(),
            result.Page, result.PageSize, result.Total));
    }

    /// <summary>
    /// Изменяет роль пользователя (например, назначение преподавателя или снятие прав администратора).
    /// Возвращает 404, если пользователь с указанным идентификатором не найден.
    /// </summary>
    /// <summary>
    /// Изменяет роль пользователя (например, назначение преподавателя или снятие прав администратора).
    /// Возвращает 404, если пользователь с указанным идентификатором не найден.
    /// </summary>
    [HttpPatch("users/{id}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetRole(Guid id, [FromQuery] UserRole role, CancellationToken ct)
    {
        return await admin.SetUserRoleAsync(id, role, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Активирует или деактивирует аккаунт пользователя.
    /// Деактивированный пользователь теряет доступ к системе, но его данные сохраняются.
    /// Возвращает 404, если пользователь не найден.
    /// </summary>
    /// <summary>
    /// Активирует или деактивирует аккаунт пользователя.
    /// Деактивированный пользователь теряет доступ к системе, но его данные сохраняются.
    /// Возвращает 404, если пользователь не найден.
    /// </summary>
    [HttpPatch("users/{id}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool active = true, CancellationToken ct = default)
    {
        return await admin.SetUserActiveAsync(id, active, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Удаляет журналы вызовов ИИ старше указанного количества дней (по умолчанию 30).
    /// Используется для освобождения места в базе данных и соблюдения политики хранения данных.
    /// Операция необратима — удалённые записи восстановить невозможно.
    /// </summary>
    /// <summary>
    /// Удаляет журналы вызовов ИИ старше указанного количества дней (по умолчанию 30).
    /// Используется для освобождения места в базе данных и соблюдения политики хранения данных.
    /// Операция необратима — удалённые записи восстановить невозможно.
    /// </summary>
    [HttpDelete("ai-logs")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PurgeAiLogs([FromQuery] int olderThanDays = 30, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-Math.Abs(olderThanDays));
        await aiDb.PurgeLogsAsync(cutoff, ct);
        return NoContent();
    }
}
