using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using System.Security.Claims;

namespace Repetitor.Api.Tests;

public sealed class TokenServiceTests
{
    private const string SigningKey = "unit_test_signing_key_that_is_long_enough_1234";

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static TokenService CreateService() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "repetitor-api",
            Audience = "repetitor-clients",
            SigningKey = SigningKey,
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        }), new FixedClock());

    private static UserTokenSubject Subject() => new(
        Guid.NewGuid(),
        "learner@example.com",
        UserRole.Learner,
        Guid.NewGuid(),
        CefrLevel.B1);

    [Fact]
    public void CreateTokenPair_IssuesAccessAndRefreshTokens()
    {
        var pair = CreateService().CreateTokenPair(Subject());

        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
        Assert.Equal(Now.AddMinutes(15), pair.AccessTokenExpiresAt);
        Assert.Equal(Now.AddDays(7), pair.RefreshTokenExpiresAt);
    }

    [Fact]
    public void BuildAccessToken_ContainsExpectedClaims()
    {
        var subject = Subject();
        var token = CreateService().BuildAccessToken(subject);

        var principal = Validate(token);

        Assert.Equal(subject.UserId.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(subject.Email, principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(subject.Email, principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal(UserRole.Learner.ToString(), principal.FindFirst(ClaimTypesRole)?.Value);
        Assert.Equal(CefrLevel.B1.ToString(), principal.FindFirst("level")?.Value);
        Assert.Equal(subject.TargetLanguageId.ToString(), principal.FindFirst("lang")?.Value);
        Assert.NotNull(principal.FindFirst(ClaimTypesNameIdentifierId)?.Value);
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value);
    }

    [Fact]
    public void BuildAccessToken_RawTokenContainsStandardRegisteredClaims()
    {
        var subject = Subject();
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            CreateService().BuildAccessToken(subject),
            ValidationParameters(SigningKey),
            out _);

        Assert.Equal(subject.UserId.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(subject.Email, principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
    }

    [Fact]
    public void BuildAccessToken_AddsSessionIdClaimWhenPresent()
    {
        var sessionId = Guid.NewGuid();
        var subject = Subject() with { SessionId = sessionId.ToString() };

        var principal = Validate(CreateService().BuildAccessToken(subject));

        Assert.Equal(sessionId.ToString(), principal.FindFirst("sid")?.Value);
    }

    [Fact]
    public void BuildAccessToken_UsesConfiguredIssuerAndAudience()
    {
        var token = CreateService().BuildAccessToken(Subject());

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("repetitor-api", jwt.Issuer);
        Assert.Contains("repetitor-clients", jwt.Audiences);
    }

    [Fact]
    public void BuildAccessToken_RejectsTokenSignedWithAnotherKey()
    {
        var token = CreateService().BuildAccessToken(Subject());
        var parameters = ValidationParameters("another_key_that_is_also_long_enough_98765");

        Assert.ThrowsAny<SecurityTokenException>(() =>
        {
            var handler = new JwtSecurityTokenHandler();
            _ = handler.ValidateToken(token, parameters, out SecurityToken? _);
        });
    }

    [Fact]
    public void CreateOpaqueToken_ReturnsTokenThatHashesToProvidedValue()
    {
        var service = CreateService();

        var token = service.CreateOpaqueToken(out var hash);

        Assert.NotEqual(token, hash);
        Assert.Equal(hash, service.HashOpaqueToken(token));
        Assert.NotEqual(hash, service.HashOpaqueToken(token + "x"));
    }

    [Fact]
    public void CreateOpaqueToken_ProducesUniqueTokens()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 50).Select(i => service.CreateOpaqueToken(out var _)).ToArray();

        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void HashOpaqueToken_IsStableForSameInput()
    {
        var service = CreateService();
        Assert.Equal(service.HashOpaqueToken("abc"), service.HashOpaqueToken("abc"));
    }

    private const string ClaimTypesRole = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string ClaimTypesNameIdentifierId = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

    private static TokenValidationParameters ValidationParameters(string key) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = "repetitor-api",
        ValidateAudience = true,
        ValidAudience = "repetitor-clients",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = false
    };

    private static ClaimsPrincipal Validate(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, ValidationParameters(SigningKey), out SecurityToken? _);
    }
}

public sealed class BcryptPasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new BcryptPasswordHasher();

    [Fact]
    public void Hash_ProducesVerifiableHash()
    {
        var hash = _hasher.Hash("Str0ngPass!23");

        Assert.StartsWith("$2", hash);
        Assert.True(_hasher.Verify("Str0ngPass!23", hash));
        Assert.False(_hasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentHashesForSamePassword()
    {
        Assert.NotEqual(_hasher.Hash("Str0ngPass!23"), _hasher.Hash("Str0ngPass!23"));
    }

    [Fact]
    public void Verify_ReturnsFalseForMalformedHash()
    {
        Assert.False(_hasher.Verify("Str0ngPass!23", "not-a-bcrypt-hash"));
    }

    [Fact]
    public void Verify_ReturnsFalseForEmptyHash()
    {
        Assert.False(_hasher.Verify("Str0ngPass!23", string.Empty));
    }
}