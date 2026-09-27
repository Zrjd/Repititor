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

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    bool NeedsRehash(string hash);
}

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

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

public interface ITokenService
{
    TokenPair CreateTokenPair(UserTokenSubject subject, Guid? previousTokenId = null);
    string CreateOpaqueToken(out string hash);
    string HashOpaqueToken(string token);
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

public sealed partial class TokenService(
    IOptions<JwtOptions> jwtOptions,
    IClock clock) : ITokenService
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public TokenPair CreateTokenPair(UserTokenSubject subject, Guid? previousTokenId = null)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        var accessToken = BuildAccessToken(subject);
        var refreshToken = CreateOpaqueToken(out _);

        return new TokenPair(accessToken, refreshToken, accessExpires, refreshExpires);
    }

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

    public string CreateOpaqueToken(out string hash)
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        var token = Base64UrlEncode(bytes);
        hash = HashOpaqueToken(token);
        return token;
    }

    public string HashOpaqueToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class CurrentUserAccessor
{
    public static Guid GetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static string? GetClaim(ClaimsPrincipal principal, string claim) =>
        principal.FindFirstValue(claim);
}
