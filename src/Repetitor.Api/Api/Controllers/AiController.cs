using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/ai")]
public sealed class AiController(
    IAiGateway gateway,
    IDbContextFactory<AppDbContext> dbFactory,
    IEmbeddingService embeddings,
    IClock clock,
    IOptions<AiOptions> aiOptions,
    IOptions<LearningOptions> learningOptions) : ControllerBase
{
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

    [HttpGet("usage")]
    [Authorize]
    [ProducesResponseType(typeof(AiUsageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiUsageResponse>> Usage([FromQuery] int days = 30, [FromQuery] bool allUsers = false, CancellationToken ct = default)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Teacher");
        var from = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-Math.Clamp(days, 1, 365) + 1);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.AiCallLogs.Where(l => l.CreatedAt >= from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (!allUsers || !isAdmin)
        {
            query = query.Where(l => l.UserId == userId);
        }

        var logs = await query.ToListAsync(ct);

        var byProvider = logs
            .GroupBy(l => l.Provider)
            .Select(g => new AiUsageByProviderResponse(g.Key, g.Count(), g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens), Math.Round(g.Sum(x => x.EstimatedCostUsd), 6)))
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

    [HttpPost("dictionary/reindex")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(typeof(ReindexResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReindexResponse>> Reindex([FromQuery] string? provider, [FromQuery] int batchSize = 64, [FromQuery] int limit = 1000, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var report = await embeddings.ReindexAsync(provider, batchSize, limit, force, ct);
        return Ok(new ReindexResponse(report.Processed, report.Created, report.Failed));
    }

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

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Teacher")]
public sealed class AdminController(
    IDbContextFactory<AppDbContext> dbFactory,
    IClock clock) : ControllerBase
{
    [HttpGet("stats")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = clock.UtcNow.AddDays(-7);

        return Ok(new
        {
            users = await db.Users.CountAsync(ct),
            activeUsers7d = await db.Users.CountAsync(u => u.LastLoginAt >= since, ct),
            languages = await db.Languages.CountAsync(ct),
            courses = await db.Courses.CountAsync(ct),
            lessons = await db.Lessons.CountAsync(ct),
            words = await db.LexicalUnits.CountAsync(u => u.Status != ContentStatus.Deprecated, ct),
            wordsWithEmbeddings = await db.LexicalUnitEmbeddings.CountAsync(ct),
            decks = await db.Decks.CountAsync(ct),
            reviewCards = await db.ReviewCards.CountAsync(ct),
            reviews7d = await db.ReviewLogs.CountAsync(r => r.ReviewedAt >= since, ct),
            exercises = await db.Exercises.CountAsync(ct),
            attempts7d = await db.ExerciseAttempts.CountAsync(a => a.CompletedAt >= since, ct),
            chatSessions = await db.ChatSessions.CountAsync(ct),
            aiCalls7d = await db.AiCallLogs.CountAsync(l => l.CreatedAt >= since, ct),
            aiCost7dUsd = Math.Round(await db.AiCallLogs.Where(l => l.CreatedAt >= since).SumAsync(l => l.EstimatedCostUsd, ct), 6)
        });
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<object>>> Users([FromQuery] PagedRequest request, [FromQuery] string? query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = TextNormalizer.Normalize(query);
            q = q.Where(u => u.Email.Contains(normalized) || u.DisplayName.ToLower().Contains(normalized));
        }

        var total = await q.CountAsync(ct);
        var page = Math.Max(request.Page, 1);
        var rows = await q
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName, Role = u.Role.ToString(), Level = u.Level.ToString(),
                u.TotalXp, u.CurrentStreak, u.IsActive, u.EmailConfirmed, u.CreatedAt, u.LastLoginAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponse<object>(rows.Cast<object>().ToArray(), page, request.PageSize, total));
    }

    [HttpPatch("users/{id}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetRole(Guid id, [FromQuery] UserRole role, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        user.Role = role;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("users/{id}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool active = true, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = active;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("ai-logs")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PurgeAiLogs([FromQuery] int olderThanDays = 30, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-Math.Abs(olderThanDays));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.AiCallLogs.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
