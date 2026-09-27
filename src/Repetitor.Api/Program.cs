using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Repetitor.Api.Api.Infrastructure;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Persistence.Seeding;
using Repetitor.Api.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("REPETITOR_");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.Configure<LearningOptions>(builder.Configuration.GetSection(LearningOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 bytes. Set it via appsettings, user-secrets or the REPETITOR_Jwt__SigningKey environment variable.");
}

var aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");

foreach (var (name, provider) in aiOptions.Providers)
{
    var kind = provider.Kind.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? "ollama" : "openai";
    builder.Services.AddHttpClient(AiHttpClientNames.For(name, kind == "ollama"), client =>
    {
        client.Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds);
    });
}

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsHistoryTable("__ef_migrations_history");
        npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
        npgsql.CommandTimeout(120);
    }));

builder.Services.AddScoped<ISrsService, SrsService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddScoped<IAiGateway, AiGateway>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IVectorSearchService, VectorSearchService>();
builder.Services.AddScoped<IExerciseGeneratorService, ExerciseGeneratorService>();
builder.Services.AddScoped<IContentGenerationService, ContentGenerationService>();
builder.Services.AddScoped<ITutorChatService, TutorChatService>();
builder.Services.AddScoped<IAnswerGradingService, AnswerGradingService>();
builder.Services.AddScoped<IPronunciationService, PronunciationService>();
builder.Services.AddScoped<IPracticeService, PracticeService>();
builder.Services.AddScoped<ISpeechService, SpeechService>();
builder.Services.AddSingleton<IMediaStorage, FileSystemMediaStorage>();
builder.Services.AddScoped<SchemaPatches>();
builder.Services.AddScoped<DatabaseInitializer>();

builder.Services.AddControllers()
    .AddJsonOptions(o => JsonOptionsSetup.Configure(o.JsonSerializerOptions));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..],
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Ошибка валидации",
            Type = "urn:repetitor:validation",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = "validation_failed";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
    };
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions.TryAdd("traceId", ctx.HttpContext.TraceIdentifier);
        ctx.ProblemDetails.Extensions.TryAdd("timestamp", DateTimeOffset.UtcNow);
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(jwtOptions.ClockSkewSeconds),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var header = context.Request.Headers["Authorization"].FirstOrDefault();
                if (!string.IsNullOrEmpty(header) && !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = header;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Learner", policy => policy.RequireRole("Learner", "Teacher", "Admin"));
});

var corsSection = builder.Configuration.GetSection("Cors");
var allowedOrigins = corsSection.Get<string[]>() ?? ["*"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Clients", policy =>
    {
        if (allowedOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swagger =>
{
    swagger.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Repetitor API",
        Version = "v1",
        Description = "REST API сервиса для изучения иностранных языков (Android / Web). " +
                      "Интервальное повторение, словарь с семантическим поиском (pgvector), " +
                      "генерация упражнений, чат-репетитор, TTS/STT и оценка произношения.",
        Contact = new OpenApiContact { Name = "Repetitor" }
    });

    swagger.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Вставьте access-токен, полученный из /api/v1/auth/login"
    });

    swagger.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", null, null)] = []
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, "Repetitor.Api.xml");
    if (File.Exists(xmlPath))
    {
        swagger.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

var healthChecks = builder.Services.AddHealthChecks();
healthChecks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
healthChecks.AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Repetitor API v1");
        options.DocumentTitle = "Repetitor API";
        options.DisplayRequestDuration();
    });
}

app.UseCors("Clients");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).ExcludeFromDescription();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = static async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        });
    }
});

app.MapGet("/openapi/v1.json", async (AppDbContext db, CancellationToken ct) =>
{
    await db.Database.CanConnectAsync(ct);
    return Results.Ok(new { openapi = "3.0.1", info = new { title = "Repetitor API", version = "v1" }, docs = "/swagger" });
}).ExcludeFromDescription();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();

public partial class Program;
