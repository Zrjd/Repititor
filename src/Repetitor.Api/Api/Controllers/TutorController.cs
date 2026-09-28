using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/tutor")]
[Authorize]
public sealed class TutorController(
    IChatDbService chat,
    IAiDbService ai,
    ITutorChatService tutor,
    IClock clock) : ControllerBase
{
    /// <summary>
    /// Возвращает список чат-сессий текущего пользователя.
    /// По умолчанию исключает архивные сессии. Параметр includeArchived
    /// позволяет получить все сессии, включая архивированные.
    /// Результат отсортирован по дате последнего сообщения, максимум 100 записей.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ChatSessionResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatSessionResponse[]>> Sessions([FromQuery] bool includeArchived = false, CancellationToken ct = default)
    {
        var sessions = await chat.GetSessionsAsync(CurrentUserAccessor.GetUserId(User), includeArchived, ct);
        return Ok(sessions.Select(s => s.ToResponse()).ToArray());
    }

    /// <summary>
    /// Создаёт новую чат-сессию с ИИ-репетитором.
    /// Инициализирует сессию с заданными параметрами: режим общения,
    /// уровень языка, сценарий и настройки провайдера ИИ.
    /// Возвращает созданную сессию с кодом 201 Created.
    /// </summary>
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ChatSessionResponse>> Start(StartChatSessionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var session = await tutor.StartSessionAsync(
            userId, request.Mode, request.Level, request.Scenario, request.Title,
            request.Provider, request.Model, request.UseDictionary, ct);

        var loaded = await chat.LoadSessionAsync(session.Id, ct);
        return StatusCode(StatusCodes.Status201Created, loaded.ToResponse());
    }

    /// <summary>
    /// Возвращает конкретную чат-сессию по идентификатору.
    /// Включает все сообщения сессии. Доступна только владельцу сессии —
    /// при попытке доступа к чужой сессии возвращается 404 Not Found.
    /// </summary>
    [HttpGet("sessions/{sessionId}")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatSessionResponse>> Session(Guid sessionId, CancellationToken ct)
    {
        var session = await chat.GetSessionAsync(CurrentUserAccessor.GetUserId(User), sessionId, ct);
        return session is null ? NotFound() : Ok(session.ToResponse());
    }

    /// <summary>
    /// Возвращает историю сообщений указанной чат-сессии.
    /// Поддерживает пагинацию через параметр before (получение сообщений
    /// раньше указанной даты) и ограничение количества через limit (1–500).
    /// Сообщения возвращаются в хронологическом порядке.
    /// </summary>
    [HttpGet("sessions/{sessionId}/messages")]
    [ProducesResponseType(typeof(ChatMessageResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatMessageResponse[]>> Messages(Guid sessionId, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!await chat.SessionExistsAsync(CurrentUserAccessor.GetUserId(User), sessionId, ct))
        {
            return NotFound();
        }

        var messages = await chat.GetMessagesAsync(sessionId, before, Math.Clamp(limit, 1, 500), ct);
        return Ok(messages.Select(m => m.ToResponse()).ToArray());
    }

    /// <summary>
    /// Отправляет сообщение в чат-сессию и получает ответ ИИ-репетитора.
    /// Возвращает пару сообщений (пользователь + ассистент), количество
    /// использованных токенов, задержку ответа и контекст из словаря (RAG),
    /// который был использован для формирования ответа.
    /// </summary>
    [HttpPost("sessions/{sessionId}/messages")]
    [ProducesResponseType(typeof(SendChatMessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SendChatMessageResponse>> Send(Guid sessionId, SendChatMessageRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var turn = await tutor.SendAsync(userId, sessionId, request.Message, request.Provider, request.Model, request.UseDictionary, ct);

        turn.UserMessage.SourceLanguageCode = request.SourceLanguageCode;
        turn.UserMessage.AudioUrl = request.AudioUrl;

        return Ok(new SendChatMessageResponse(
            turn.UserMessage.ToResponse(),
            turn.AssistantMessage.ToResponse(),
            turn.InputTokens,
            turn.OutputTokens,
            turn.LatencyMs,
            turn.RagContext.Select(r => new RagContextResponse(
                r.LexicalUnitId, r.Text, r.Translation, r.Transcription, r.Similarity)).ToArray()));
    }

    /// <summary>
    /// Отправляет сообщение в чат-сессию и стримит ответ ИИ в реальном времени.
    /// Использует формат Server-Sent Events (SSE) для передачи частей ответа
    /// по мере их генерации. События: delta (фрагмент текста), done (полный текст),
    /// error (сообщение об ошибке). Это создаёт эффект "печатания" в интерфейсе.
    /// </summary>
    [HttpPost("sessions/{sessionId}/messages/stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task Stream(Guid sessionId, SendChatMessageRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache,no-store";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            var builder = new StringBuilder();
            await foreach (var chunk in tutor.StreamAsync(userId, sessionId, request.Message, request.Provider, request.Model, request.UseDictionary, ct))
            {
                builder.Append(chunk);
                await WriteSseAsync("delta", chunk, ct);
            }

            await WriteSseAsync("done", builder.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await WriteSseAsync("error", ex.Message, ct);
        }
    }

    /// <summary>
    /// Перегенерирует ответ ИИ на указанное сообщение пользователя.
    /// Полезно, когда пользователь хочет получить альтернативный вариант ответа.
    /// Заменяет существующее сообщение ассистента новым ответом,
    /// сохраняя контекст диалога.
    /// </summary>
    [HttpPost("sessions/{sessionId}/messages/{messageId}/regenerate")]
    [ProducesResponseType(typeof(ChatMessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatMessageResponse>> Regenerate(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var message = await tutor.RegenerateAsync(userId, sessionId, messageId, ct);
        return Ok(message.ToResponse());
    }

    /// <summary>
    /// Переименовывает чат-сессию.
    /// Позволяет пользователю задать понятное название для сессии,
    /// чтобы было проще ориентироваться в списке диалогов.
    /// Возвращает обновлённую сессию с сообщениями.
    /// </summary>
    [HttpPatch("sessions/{sessionId}")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatSessionResponse>> UpdateSession(Guid sessionId, RenameSessionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await tutor.RenameAsync(userId, sessionId, request.Title, ct);

        var session = await chat.GetSessionAsync(userId, sessionId, ct);
        return session is null ? NotFound() : Ok(session.ToResponse());
    }

    /// <summary>
    /// Архивирует или разархивирует чат-сессию.
    /// Архивные сессии скрываются из основного списка, но сохраняются
    /// в базе данных. Параметр archived=false возвращает сессию в активные.
    /// </summary>
    [HttpPost("sessions/{sessionId}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid sessionId, [FromQuery] bool archived = true, CancellationToken ct = default)
    {
        var updated = await chat.SetArchivedAsync(CurrentUserAccessor.GetUserId(User), sessionId, archived, ct);
        return updated ? NoContent() : NotFound();
    }

    /// <summary>
    /// Полностью удаляет чат-сессию и все её сообщения.
    /// Операция необратима — данные удаляются из базы навсегда.
    /// Доступна только владельцу сессии.
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var deleted = await chat.DeleteSessionAsync(CurrentUserAccessor.GetUserId(User), sessionId, ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Сохраняет оценку пользователем ответа ИИ-репетитора.
    /// Используется для сбора обратной связи о качестве ответов,
    /// что помогает улучшать систему и анализировать эффективность ИИ.
    /// </summary>
    [HttpPost("messages/{messageId}/feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Feedback(Guid messageId, FeedbackRequest request, CancellationToken ct)
    {
        var saved = await chat.SetMessageFeedbackAsync(CurrentUserAccessor.GetUserId(User), messageId, request.Rating, ct);
        return saved ? NoContent() : NotFound();
    }

    /// <summary>
    /// Возвращает статистику использования ИИ-сервисов пользователем.
    /// Включает количество вызовов, потреблённые токены, оценочную стоимость
    /// и разбивку по провайдерам и дням. Параметр days определяет период
    /// анализа (1–365 дней). Помогает контролировать расход ресурсов.
    /// </summary>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(AiUsageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiUsageResponse>> Usage([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var report = await ai.GetUsageReportAsync(CurrentUserAccessor.GetUserId(User), today.AddDays(-Math.Clamp(days, 1, 365) + 1), today, ct);

        return Ok(new AiUsageResponse(
            report.From, report.To,
            report.TotalCalls, report.FailedCalls,
            report.InputTokens, report.OutputTokens, report.EstimatedCostUsd,
            report.ByProvider.Select(p => new AiUsageByProviderResponse(
                p.Provider, p.Calls, p.InputTokens, p.OutputTokens, p.EstimatedCostUsd)).ToArray(),
            report.ByDay.Select(d => new AiUsageByDayResponse(d.Date, d.Calls, d.InputTokens, d.OutputTokens)).ToArray()));
    }

    private async Task WriteSseAsync(string eventName, string data, CancellationToken ct)
    {
        var payload = new StringBuilder();
        payload.Append("event: ").Append(eventName).Append('\n');
        foreach (var line in data.Split('\n'))
        {
            payload.Append("data: ").Append(line).Append('\n');
        }

        payload.Append('\n');
        await Response.WriteAsync(payload.ToString(), ct);
        await Response.Body.FlushAsync(ct);
    }
}
