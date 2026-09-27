using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/tutor")]
[Authorize]
public sealed class TutorController(
    IDbContextFactory<AppDbContext> dbFactory,
    ITutorChatService tutor,
    IClock clock) : ControllerBase
{
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ChatSessionResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatSessionResponse[]>> Sessions([FromQuery] bool includeArchived = false, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var sessions = await db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.UserId == userId && (includeArchived || !s.IsArchived))
            .OrderByDescending(s => s.LastMessageAt)
            .Take(100)
            .ToListAsync(ct);

        return Ok(sessions.Select(s => s.ToResponse()).ToArray());
    }

    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ChatSessionResponse>> Start(StartChatSessionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var session = await tutor.StartSessionAsync(
            userId, request.Mode, request.Level, request.Scenario, request.Title,
            request.Provider, request.Model, request.UseDictionary, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var loaded = await db.ChatSessions.Include(s => s.Messages).FirstAsync(s => s.Id == session.Id, ct);
        return StatusCode(StatusCodes.Status201Created, loaded.ToResponse());
    }

    [HttpGet("sessions/{sessionId}")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatSessionResponse>> Session(Guid sessionId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        return session is null ? NotFound() : Ok(session.ToResponse());
    }

    [HttpGet("sessions/{sessionId}/messages")]
    [ProducesResponseType(typeof(ChatMessageResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatMessageResponse[]>> Messages(Guid sessionId, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var exists = await db.ChatSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (!exists)
        {
            return NotFound();
        }

        var query = db.ChatMessages.Where(m => m.SessionId == sessionId);
        if (before is { } b)
        {
            query = query.Where(m => m.CreatedAt < b);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        return Ok(messages.OrderBy(m => m.CreatedAt).Select(m => m.ToResponse()).ToArray());
    }

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

    [HttpPost("sessions/{sessionId}/messages/{messageId}/regenerate")]
    [ProducesResponseType(typeof(ChatMessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatMessageResponse>> Regenerate(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var message = await tutor.RegenerateAsync(userId, sessionId, messageId, ct);
        return Ok(message.ToResponse());
    }

    [HttpPatch("sessions/{sessionId}")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatSessionResponse>> UpdateSession(Guid sessionId, RenameSessionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await tutor.RenameAsync(userId, sessionId, request.Title, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        return session is null ? NotFound() : Ok(session.ToResponse());
    }

    [HttpPost("sessions/{sessionId}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid sessionId, [FromQuery] bool archived = true, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return NotFound();
        }

        session.IsArchived = archived;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("sessions/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return NotFound();
        }

        db.ChatSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("messages/{messageId}/feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Feedback(Guid messageId, FeedbackRequest request, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.Session!.UserId == userId, ct);

        if (message is null)
        {
            return NotFound();
        }

        message.FeedbackRating = request.Rating;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("usage")]
    [ProducesResponseType(typeof(AiUsageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiUsageResponse>> Usage([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var from = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-Math.Clamp(days, 1, 365) + 1);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var logs = await db.AiCallLogs
            .Where(l => l.UserId == userId && l.CreatedAt >= from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            .ToListAsync(ct);

        var byProvider = logs
            .GroupBy(l => l.Provider)
            .Select(g => new AiUsageByProviderResponse(
                g.Key, g.Count(), g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens),
                Math.Round(g.Sum(x => x.EstimatedCostUsd), 6)))
            .ToArray();

        var byDay = logs
            .GroupBy(l => DateOnly.FromDateTime(l.CreatedAt.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new AiUsageByDayResponse(g.Key, g.Count(), g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens)))
            .ToArray();

        return Ok(new AiUsageResponse(
            from, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            logs.Count, logs.Count(l => !l.Success),
            logs.Sum(l => l.InputTokens), logs.Sum(l => l.OutputTokens),
            Math.Round(logs.Sum(l => l.EstimatedCostUsd), 6), byProvider, byDay));
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
