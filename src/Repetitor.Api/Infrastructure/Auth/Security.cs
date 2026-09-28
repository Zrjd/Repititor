using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.Auth;

/// <summary>
/// Интерфейс для хеширования и проверки паролей. Абстрагирует алгоритм хеширования от остального кода.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Создаёт хеш пароля для сохранения в БД. Используется при регистрации и смене пароля.
    /// </summary>
    string Hash(string password);
    /// <summary>
    /// Проверяет, соответствует ли пароль сохранённому хешу. Возвращает false для пустого хеша или при ошибке разбора.
    /// </summary>
    bool Verify(string password, string hash);
    /// <summary>
    /// Определяет, нужен ли пересчёт хеша (например, при изменении параметров алгоритма). Возвращает true для пустого/невалидного хеша.
    /// </summary>
    bool NeedsRehash(string hash);
}

/// <summary>
/// Реализация IPasswordHasher на основе алгоритма BCrypt с коэффициентом сложности 12.
/// BCrypt — устойчивый к перебору алгоритм хеширования паролей со встроенной солью.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <summary>
    /// Создаёт хеш пароля с использованием BCrypt и текущим коэффициентом сложности.
    /// </summary>
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>
    /// Проверяет пароль по сохранённому BCrypt-хешу. Обрабатывает исключения разбора, возвращая false при невалидном хеше.
    /// </summary>
    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Проверяет, устарел ли хеш (например, изменился WorkFactor). При невалидном хеше возвращает true.
    /// </summary>
    public bool NeedsRehash(string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return true;
        }

        try
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hash, WorkFactor);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException)
        {
            return true;
        }
    }
}

/// <summary>
/// Интерфейс для создания и управления токенами аутентификации (access и refresh).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Создаёт новую пару токенов (access + refresh) для указанного пользователя. Используется при входе и обновлении токенов.
    /// </summary>
    TokenPair CreateTokenPair(UserTokenSubject subject, Guid? previousTokenId = null);
    /// <summary>
    /// Генерирует случайный непрозрачный токен и возвращает его вместе с хешем для сохранения в БД.
    /// </summary>
    string CreateOpaqueToken(out string hash);
    /// <summary>
    /// Вычисляет SHA256-хеш непрозрачного токена для безопасного хранения в БД.
    /// </summary>
    string HashOpaqueToken(string token);
    /// <summary>
    /// Создаёт JWT access-токен с утверждениями о пользователе (ID, email, роль, язык, уровень).
    /// </summary>
    string BuildAccessToken(UserTokenSubject subject);
}

public sealed record UserTokenSubject(
    Guid UserId,
    string Email,
    UserRole Role,
    Guid TargetLanguageId,
    CefrLevel Level,
    string? SessionId = null);

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Реализация ITokenService, создающая JWT access-токены и случайные refresh-токены.
/// Использует IClock для тестируемости и JwtOptions из конфигурации.
/// </summary>
public sealed partial class TokenService(
    IOptions<JwtOptions> jwtOptions,
    IClock clock) : ITokenService
{
    private readonly JwtOptions _options = jwtOptions.Value;

    /// <summary>
    /// Создаёт пару токенов: краткоживущий JWT access-токен и долгоживущий случайный refresh-токен.
    /// Возвращает оба токена и сроки их действия.
    /// </summary>
    public TokenPair CreateTokenPair(UserTokenSubject subject, Guid? previousTokenId = null)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        var accessToken = BuildAccessToken(subject);
        var refreshToken = CreateOpaqueToken(out _);

        return new TokenPair(accessToken, refreshToken, accessExpires, refreshExpires);
    }

    /// <summary>
    /// Создаёт и подписывает JWT access-токен с утверждениями из объекта UserTokenSubject.
    /// Срок действия определяется настройками AccessTokenMinutes. Включает claims: sub, email, role, lang, level, jti.
    /// </summary>
    public string BuildAccessToken(UserTokenSubject subject)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, subject.Email),
            new(ClaimTypes.NameIdentifier, subject.UserId.ToString()),
            new(ClaimTypes.Name, subject.Email),
            new(ClaimTypes.Role, subject.Role.ToString()),
            new("lang", subject.TargetLanguageId.ToString()),
            new("level", subject.Level.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        if (subject.SessionId is not null)
        {
            claims.Add(new Claim("sid", subject.SessionId));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: clock.UtcNow.UtcDateTime,
            expires: clock.UtcNow.AddMinutes(_options.AccessTokenMinutes).UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Генерирует криптографически случайный 48-байтный токен и возвращает его вместе с SHA256-хешем.
    /// Сам токен передаётся клиенту, а хеш сохраняется в БД для последующей проверки.
    /// </summary>
    public string CreateOpaqueToken(out string hash)
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        var token = Base64UrlEncode(bytes);
        hash = HashOpaqueToken(token);
        return token;
    }

    /// <summary>
    /// Вычисляет SHA256-хеш токена и возвращает его в шестнадцатеричном виде.
    /// Используется для проверки входящего токена против сохранённого в БД хеша.
    /// </summary>
    public string HashOpaqueToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Абстракция над системными часами для тестируемости операций с временем.
/// </summary>
public interface IClock
{
    /// <summary>Текущее время в формате UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Реализация IClock на основе системного времени.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>Текущее время UTC из системы.</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Вспомогательный класс для извлечения данных текущего пользователя из ClaimsPrincipal.
/// </summary>
public sealed class CurrentUserAccessor
{
    /// <summary>
    /// Извлекает GUID идентификатор пользователя из claims токена.
    /// Проверяет оба стандартных claim-типа: NameIdentifier и Sub.
    /// Возвращает Guid.Empty, если claim отсутствует или не является валидным GUID.
    /// </summary>
    public static Guid GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Возвращает значение произвольного claim из токена пользователя или null, если claim отсутствует.
    /// </summary>
    public static string? GetClaim(ClaimsPrincipal principal, string claim) =>
        principal.FindFirstValue(claim);
}
