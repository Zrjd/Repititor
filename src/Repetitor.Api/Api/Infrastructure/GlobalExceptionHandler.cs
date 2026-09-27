using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Infrastructure;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, type, code) = Map(exception);

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("{Code} on {Method} {Path}: {Message}", code, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = environment.IsDevelopment() || status < 500 ? exception.Message : null,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static (int Status, string Title, string Type, string Code) Map(Exception exception) => exception switch
    {
        ChatSessionNotFoundException => (404, "Диалог не найден", "urn:repetitor:chat-session-not-found", "chat_session_not_found"),
        ChatMessageNotFoundException => (404, "Сообщение не найдено", "urn:repetitor:chat-message-not-found", "chat_message_not_found"),
        UnsupportedExerciseTypeException => (400, "Тип упражнения не поддерживается", "urn:repetitor:unsupported-exercise-type", "unsupported_exercise_type"),
        MediaTooLargeException => (413, "Файл слишком большой", "urn:repetitor:media-too-large", "media_too_large"),
        UnsupportedMediaTypeException => (415, "Неподдерживаемый формат аудио", "urn:repetitor:unsupported-media-type", "unsupported_media_type"),
        UnauthorizedAccessException => (403, "Доступ запрещён", "urn:repetitor:forbidden", "forbidden"),
        FileNotFoundException => (404, "Файл не найден", "urn:repetitor:file-not-found", "file_not_found"),
        ArgumentException => (400, "Некорректный запрос", "urn:repetitor:bad-request", "bad_request"),
        KeyNotFoundException => (404, "Объект не найден", "urn:repetitor:not-found", "not_found"),
        InvalidOperationException => (409, "Операция невозможна", "urn:repetitor:conflict", "conflict"),
        DbUpdateConcurrencyException => (409, "Конфликт обновления", "urn:repetitor:concurrency", "concurrency_conflict"),
        OperationCanceledException => (499, "Запрос отменён", "urn:repetitor:cancelled", "cancelled"),
        AiProviderException ai => ai.StatusCode is { } sc
            ? sc switch
            {
                System.Net.HttpStatusCode.TooManyRequests => (429, "Превышен лимит запросов к ИИ", "urn:repetitor:ai-rate-limit", "ai_rate_limited"),
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                    (502, "Ошибка авторизации у ИИ-провайдера", "urn:repetitor:ai-auth", "ai_auth_failed"),
                _ => (502, "ИИ-провайдер недоступен", "urn:repetitor:ai-unavailable", "ai_unavailable")
            }
            : (502, "ИИ-провайдер недоступен", "urn:repetitor:ai-unavailable", "ai_unavailable"),
        _ => (500, "Внутренняя ошибка сервера", "urn:repetitor:internal", "internal_error")
    };
}

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            var level = context.Response.StatusCode >= 500
                ? LogLevel.Error
                : context.Response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information;

            logger.Log(level, "{Method} {Path} -> {Status} in {Elapsed}ms",
                context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }
}

public sealed class JsonOptionsSetup
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    }
}
