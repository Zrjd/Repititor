using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Infrastructure;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IDbContextFactory<AppDbContext> dbFactory,
    ITokenService tokens,
    IPasswordHasher hasher,
    IClock clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LearningOptions> learningOptions,
    IEmailService emailService,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!IsStrongEnough(request.Password))
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]>
            {
                ["password"] =
                [
                    "Пароль должен содержать минимум 8 символов, буквы разных регистров и цифры."
                ]
            });
        }

        var options = learningOptions.Value;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Conflict(new ErrorResponse("email_taken", "Пользователь с таким email уже существует."));
        }

        var languages = await db.Languages.OrderBy(l => l.SortOrder).ToListAsync(ct);
        if (languages.Count == 0)
        {
            return Problem("Справочник языков не инициализирован.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var target = request.TargetLanguageId != Guid.Empty && languages.Any(l => l.Id == request.TargetLanguageId)
            ? languages.First(l => l.Id == request.TargetLanguageId)
            : languages.First(l => l.Code == "en");
        var interfaceLang = request.InterfaceLanguageId != Guid.Empty && languages.Any(l => l.Id == request.InterfaceLanguageId)
            ? languages.First(l => l.Id == request.InterfaceLanguageId)
            : languages.FirstOrDefault(l => l.Code == "ru") ?? languages[0];

        var user = new User
        {
            Email = email,
            PasswordHash = hasher.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
            TargetLanguageId = target.Id,
            InterfaceLanguageId = interfaceLang.Id,
            Level = request.Level,
            TargetLevel = NextLevel(request.Level),
            DailyGoalXp = request.DailyGoalXp > 0 ? request.DailyGoalXp : options.DefaultDailyGoalXp,
            LastLoginAt = clock.UtcNow
        };
        user.InterfaceLanguage = interfaceLang;
        user.TargetLanguage = target;

        db.Users.Add(user);

        var firstDeck = new Deck
        {
            UserId = user.Id,
            Name = "Мои слова",
            Description = "Слова, добавленные вручную или найденные через поиск",
            LanguageCode = target.Code
        };
        db.Decks.Add(firstDeck);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Registered user {UserId} ({Email})", user.Id, user.Email);
        return await IssueAsync(db, user, target, interfaceLang, ct);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new ErrorResponse("invalid_credentials", "Неверный email или пароль."));
        }

        if (!user.IsActive)
        {
            return Forbid();
        }

        if (hasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = hasher.Hash(request.Password);
        }

        user.LastLoginAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await IssueAsync(db, user, user.TargetLanguage!, user.InterfaceLanguage!, ct);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.HashOpaqueToken(request.RefreshToken);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u!.TargetLanguage)
            .Include(t => t.User).ThenInclude(u => u!.InterfaceLanguage)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || stored.RevokedAt is not null)
        {
            return Unauthorized(new ErrorResponse("invalid_refresh_token", "Токен недействителен."));
        }

        if (stored.ExpiresAt <= clock.UtcNow)
        {
            return Unauthorized(new ErrorResponse("expired_refresh_token", "Срок действия токена истёк."));
        }

        var user = stored.User!;
        if (!user.IsActive)
        {
            return Forbid();
        }

        stored.RevokedAt = clock.UtcNow;
        stored.RevokedByRotation = true;

        var rotated = tokens.CreateOpaqueToken(out var newHash);
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresAt = clock.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays),
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ReplacedByTokenId = null
        };
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        stored.ReplacedByTokenId = entity.Id;
        await db.SaveChangesAsync(ct);

        var pair = tokens.CreateTokenPair(Subject(user));
        return Ok(BuildResponse(pair, user, user.TargetLanguage!, user.InterfaceLanguage!));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var raw = Request.Headers["X-Refresh-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = request?.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return NoContent();
        }

        var hash = tokens.HashOpaqueToken(raw);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var userId = CurrentUserAccessor.GetUserId(User);
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), ct);

        return NoContent();
    }

    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .Include(u => u.TargetLanguage)
            .Include(u => u.InterfaceLanguage)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null ? Unauthorized() : Ok(user.ToResponse());
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!IsStrongEnough(request.NewPassword))
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]>
            {
                ["newPassword"] = ["Пароль должен содержать минимум 8 символов, буквы разных регистров и цифры."]
            });
        }

        var userId = CurrentUserAccessor.GetUserId(User);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["currentPassword"] = ["Неверный текущий пароль."] });
        }

        user.PasswordHash = hasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(ct);

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), ct);

        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);
        if (user is not null)
        {
            var token = tokens.CreateOpaqueToken(out var hash);
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = hash,
                ExpiresAt = clock.UtcNow.AddHours(2)
            });
            await db.SaveChangesAsync(ct);

            var baseUrl = jwtOptions.Value.ResetPasswordUrl;
            var resetLink = $"{baseUrl}?token={Uri.EscapeDataString(token)}";
            await emailService.SendPasswordResetAsync(user.Email, resetLink, ct);
        }

        return Accepted(new { message = "Если аккаунт существует, ссылка для сброса отправлена." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        if (!IsStrongEnough(request.NewPassword))
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["newPassword"] = ["Слишком простой пароль."] });
        }

        var hash = tokens.HashOpaqueToken(request.Token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var record = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null, ct);

        if (record is null || record.ExpiresAt <= clock.UtcNow)
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["token"] = ["Токен недействителен или истёк."] });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == record.UserId, ct);
        if (user is null)
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["token"] = ["Токен недействителен."] });
        }

        user.PasswordHash = hasher.Hash(request.NewPassword);
        record.UsedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), ct);

        return NoContent();
    }

    private async Task<ActionResult<TokenResponse>> IssueAsync(
        AppDbContext db,
        User user,
        Language target,
        Language interfaceLanguage,
        CancellationToken ct)
    {
        var pair = tokens.CreateTokenPair(Subject(user));
        var refresh = tokens.CreateOpaqueToken(out var hash);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = pair.RefreshTokenExpiresAt,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync(ct);
        return Ok(BuildResponse(pair with { RefreshToken = refresh }, user, target, interfaceLanguage));
    }

    private UserTokenSubject Subject(User user) => new(
        user.Id, user.Email, user.Role, user.TargetLanguageId, user.Level);

    private static TokenResponse BuildResponse(TokenPair pair, User user, Language target, Language interfaceLanguage) => new(
        pair.AccessToken,
        pair.RefreshToken,
        "Bearer",
        (int)(pair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
        pair.AccessTokenExpiresAt,
        new UserResponse(
            user.Id, user.Email, user.DisplayName, user.Role.ToString(), user.AvatarUrl,
            user.TargetLanguageId, target.Code, target.NameRussian,
            user.InterfaceLanguageId, user.Level.ToString(), user.TargetLevel.ToString(),
            user.DailyGoalXp, user.SpeechRate, user.TotalXp, user.CurrentStreak, user.LongestStreak,
            user.PreferredAiProvider, user.EmailConfirmed, user.CreatedAt));

    internal static bool IsStrongEnough(string password) =>
        password.Length >= 8
        && password.Any(char.IsLetter)
        && password.Any(char.IsUpper)
        && password.Any(char.IsDigit);

    internal static CefrLevel NextLevel(CefrLevel level) => level switch
    {
        CefrLevel.A1 => CefrLevel.A2,
        CefrLevel.A2 => CefrLevel.B1,
        CefrLevel.B1 => CefrLevel.B2,
        CefrLevel.B2 => CefrLevel.C1,
        CefrLevel.C1 => CefrLevel.C2,
        _ => CefrLevel.C2
    };
}