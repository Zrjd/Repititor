using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Infrastructure;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IUserDbService users,
    ICatalogDbService catalog,
    ITokenService tokens,
    IPasswordHasher hasher,
    IClock clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LearningOptions> learningOptions,
    IEmailService emailService,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// Регистрирует нового пользователя в системе.
    /// Создаёт аккаунт с указанным email и паролем, проверяет уникальность email,
    /// устанавливает языки по умолчанию и выдаёт пару токенов доступа.
    /// Также создаёт стартодеку "Мои слова" для нового пользователя.
    /// </summary>
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

        if (await users.EmailExistsAsync(email, ct))
        {
            return Conflict(new ErrorResponse("email_taken", "Пользователь с таким email уже существует."));
        }

        var languages = await catalog.GetAllLanguagesAsync(ct);
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

        await users.CreateWithFirstDeckAsync(
            user,
            "Мои слова",
            "Слова, добавленные вручную или найденные через поиск",
            target.Code,
            ct);

        logger.LogInformation("Registered user {UserId} ({Email})", user.Id, user.Email);
        return await IssueAsync(user, target, interfaceLang, ct);
    }

    /// <summary>
    /// Аутентифицирует пользователя по email и паролю.
    /// Проверяет учётные данные, при необходимости перехеширует пароль
    /// и выдаёт новую пару токенов доступа (access + refresh).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email, ct);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new ErrorResponse("invalid_credentials", "Неверный email или пароль."));
        }

        if (!user.IsActive)
        {
            return Forbid();
        }

        // Хеш пересчитывается только если параметры алгоритма изменились, иначе остаётся прежним.
        var rehashed = hasher.NeedsRehash(user.PasswordHash) ? hasher.Hash(request.Password) : null;
        await users.TouchLastLoginAsync(user.Id, clock.UtcNow, rehashed, ct);

        return await IssueAsync(user, user.TargetLanguage!, user.InterfaceLanguage!, ct);
    }

    /// <summary>
    /// Обновляет пару токенов доступа по refresh-токену.
    /// Реализует механизм ротации: старый refresh-токен отзывается,
    /// а пользователь получает новую пару токенов. Это повышает безопасность,
    /// предотвращая повторное использование скомпрометированных токенов.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.HashOpaqueToken(request.RefreshToken);
        var stored = await users.FindRefreshTokenAsync(hash, ct);

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

        tokens.CreateOpaqueToken(out var newHash);
        await users.RotateRefreshTokenAsync(
            stored.Id,
            user.Id,
            newHash,
            clock.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays),
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            clock.UtcNow,
            ct);

        var pair = tokens.CreateTokenPair(Subject(user));
        return Ok(BuildResponse(pair, user, user.TargetLanguage!, user.InterfaceLanguage!));
    }

    /// <summary>
    /// Завершает текущую сессию пользователя, отзывая указанный refresh-токен.
    /// Токен может быть передан в заголовке X-Refresh-Token или в теле запроса.
    /// Если токен не передан, запрос считается успешным без ошибки.
    /// </summary>
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

        var userId = CurrentUserAccessor.GetUserId(User);
        await users.RevokeRefreshTokenAsync(userId, tokens.HashOpaqueToken(raw), clock.UtcNow, ct);

        return NoContent();
    }

    /// <summary>
    /// Завершает все активные сессии пользователя, отзывая все его refresh-токены.
    /// Используется, например, при подозрении на компрометацию аккаунта
    /// или при смене пароля для принудительного перелогина на всех устройствах.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await users.RevokeAllRefreshTokensAsync(CurrentUserAccessor.GetUserId(User), clock.UtcNow, ct);
        return NoContent();
    }

    /// <summary>
    /// Возвращает профиль текущего аутентифицированного пользователя.
    /// Содержит информацию о пользователе, включая языки обучения,
    /// уровень, статистику и настройки. Используется для отображения
    /// личного кабинета и настроек профиля.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var user = await users.FindWithLanguagesAsync(CurrentUserAccessor.GetUserId(User), ct);
        return user is null ? Unauthorized() : Ok(user.ToResponse());
    }

    /// <summary>
    /// Изменяет пароль текущего пользователя.
    /// Проверяет текущий пароль, валидирует новый на сложность,
    /// обновляет хеш пароля и отзывает все активные сессии для безопасности.
    /// </summary>
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
        var user = await users.FindAsync(userId, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["currentPassword"] = ["Неверный текущий пароль."] });
        }

        await users.SetPasswordHashAsync(userId, hasher.Hash(request.NewPassword), ct);
        await users.RevokeAllRefreshTokensAsync(userId, clock.UtcNow, ct);

        return NoContent();
    }

    /// <summary>
    /// Инициирует процесс восстановления пароля.
    /// Генерирует токен сброса с ограниченным сроком действия (2 часа)
    /// и отправляет письмо со ссылкой на указанный email.
    /// Всегда возвращает успешный ответ, чтобы не раскрывать существование аккаунта.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.FindActiveByEmailAsync(email, ct);
        if (user is not null)
        {
            var token = tokens.CreateOpaqueToken(out var hash);
            await users.AddPasswordResetTokenAsync(user.Id, hash, clock.UtcNow.AddHours(2), ct);

            var baseUrl = jwtOptions.Value.ResetPasswordUrl;
            var resetLink = $"{baseUrl}?token={Uri.EscapeDataString(token)}";
            await emailService.SendPasswordResetAsync(user.Email, resetLink, ct);
        }

        return Accepted(new { message = "Если аккаунт существует, ссылка для сброса отправлена." });
    }

    /// <summary>
    /// Сбрасывает пароль пользователя по токену восстановления.
    /// Проверяет валидность и срок действия токена, обновляет пароль
    /// и отзывает все активные сессии. Токен помечается использованным,
    /// предотвращая повторное использование.
    /// </summary>
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
        var record = await users.FindPasswordResetTokenAsync(hash, ct);

        if (record is null || record.ExpiresAt <= clock.UtcNow)
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["token"] = ["Токен недействителен или истёк."] });
        }

        var user = await users.FindAsync(record.UserId, ct);
        if (user is null)
        {
            return ApiValidation.Invalid(new Dictionary<string, string[]> { ["token"] = ["Токен недействителен."] });
        }

        await users.CompletePasswordResetAsync(record.Id, user.Id, hasher.Hash(request.NewPassword), clock.UtcNow, ct);
        await users.RevokeAllRefreshTokensAsync(user.Id, clock.UtcNow, ct);

        return NoContent();
    }

    /// <summary>
    /// Выпускает пару токенов для пользователя: подписанный access-токен и новый refresh-токен,
    /// который сразу сохраняется вместе с данными устройства для возможности отзыва.
    /// </summary>
    private async Task<ActionResult<TokenResponse>> IssueAsync(
        User user,
        Language target,
        Language interfaceLanguage,
        CancellationToken ct)
    {
        var pair = tokens.CreateTokenPair(Subject(user));
        var refresh = tokens.CreateOpaqueToken(out var hash);
        await users.AddRefreshTokenAsync(
            user.Id,
            hash,
            pair.RefreshTokenExpiresAt,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

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
