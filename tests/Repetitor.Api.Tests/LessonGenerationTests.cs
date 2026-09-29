using System.Net;
using System.Text.Json;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Tests;

public sealed class LessonGenerationStateTests
{
    [Theory]
    [InlineData(LessonGenerationStatus.Queued, true)]
    [InlineData(LessonGenerationStatus.Running, true)]
    [InlineData(LessonGenerationStatus.Completed, false)]
    [InlineData(LessonGenerationStatus.Failed, false)]
    [InlineData(LessonGenerationStatus.None, false)]
    public void IsActive_OnlyForPendingWork(LessonGenerationStatus status, bool expected)
    {
        var lessonId = Guid.NewGuid();
        var requestedAt = DateTimeOffset.UtcNow;
        var state = new LessonGenerationState(lessonId, status, requestedAt, null, null);

        Assert.Equal(expected, state.IsActive);
    }

    [Fact]
    public void ToResponse_ExposesEnumNameAsStatus()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var completedAt = requestedAt.AddMinutes(2);
        var state = new LessonGenerationState(
            Guid.NewGuid(), LessonGenerationStatus.Failed, requestedAt, completedAt, "provider timeout");

        var response = state.ToResponse();

        Assert.Equal("Failed", response.Status);
        Assert.Equal(requestedAt, response.RequestedAt);
        Assert.Equal(completedAt, response.CompletedAt);
        Assert.Equal("provider timeout", response.Error);
        Assert.False(response.IsActive);
    }
}

public sealed class LessonGenerationJsonTests
{
    [Fact]
    public void Read_RestoresEveryField()
    {
        var request = new LessonGenerationRequest(
            "Present Simple", CefrLevel.A2, "с примерами", 25, "краткое описание",
            "ollama", "qwen3", "base prompt");

        var restored = LessonGenerationJson.Read(LessonGenerationJson.Write(request));

        Assert.Equal(request, restored);
    }

    [Fact]
    public void Read_KeepsMinimalRequest()
    {
        var request = new LessonGenerationRequest(null, null, null, null, null, null, null, null);

        var restored = LessonGenerationJson.Read(LessonGenerationJson.Write(request));

        Assert.Equal(request, restored);
    }

    [Fact]
    public void Read_IgnoresUnknownLevel()
    {
        var json = LessonGenerationJson.Write(new LessonGenerationRequest(null, null, null, null, null, null, null, null));
        json["level"] = "Klingon";

        Assert.Null(LessonGenerationJson.Read(json).Level);
    }

    [Fact]
    public void Read_IgnoresNonNumericDuration()
    {
        var json = LessonGenerationJson.Write(new LessonGenerationRequest(null, null, null, null, null, null, null, null));
        json["durationMinutes"] = "long";

        Assert.Null(LessonGenerationJson.Read(json).DurationMinutes);
    }

    [Fact]
    public void Read_RejectsNonPositiveDuration()
    {
        var json = LessonGenerationJson.Write(new LessonGenerationRequest(null, null, null, 0, null, null, null, null));

        Assert.Null(LessonGenerationJson.Read(json).DurationMinutes);
    }
}

public sealed class AdminLessonMappingTests
{
    private static AdminLessonItem Lesson(
        bool lessonPublished,
        LessonGenerationStatus status = LessonGenerationStatus.None) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "lesson-slug",
        "Present Simple",
        "summary",
        "# content",
        1,
        15,
        lessonPublished,
        null,
        ["present", "simple"],
        status,
        DateTimeOffset.UtcNow,
        null,
        null,
        true);

    [Fact]
    public void LessonResponse_CarriesGenerationState()
    {
        var lesson = Lesson(lessonPublished: true, status: LessonGenerationStatus.Completed);

        var response = new AdminLessonResponse(
            lesson.Id, lesson.CourseId, lesson.Slug, lesson.Title, lesson.Summary, lesson.ContentMarkdown,
            lesson.SortOrder, lesson.EstimatedMinutes, lesson.IsPublished, lesson.GrammarTopicId,
            lesson.KeyVocabulary, lesson.AiGenerationStatus.ToString(), lesson.AiGenerationRequestedAt,
            lesson.AiGenerationCompletedAt, lesson.AiGenerationError, lesson.IsAvailableToStudents);

        Assert.Equal("Completed", response.AiGenerationStatus);
        Assert.NotNull(response.AiGenerationRequestedAt);
        Assert.Null(response.AiGenerationError);
        Assert.True(response.IsAvailableToStudents);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void DraftFlag_FollowsLessonAndCoursePublication(
        bool lessonPublished, bool coursePublished, bool expected)
    {
        Assert.Equal(expected, LessonAvailability.IsAvailableToStudents(lessonPublished, coursePublished));
    }
}

public sealed class LessonGenerationFailureTests
{
    [Fact]
    public void ProviderTimeout_IsReportedAsAModelTimeout()
    {
        // HttpClient при своём таймауте бросает TaskCanceledException, под ним TimeoutException,
        // а дальше ещё несколько обёрток вплоть до сокета.
        var exception = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.",
            new TimeoutException(
                "The operation was canceled.",
                new TaskCanceledException(
                    "The operation was canceled.",
                    new IOException("Unable to read data from the transport connection: Operation canceled."))));

        var message = LessonGenerationFailure.Describe(exception, 900);

        Assert.Equal("Модель не ответила за 900 с. Для локальной модели увеличьте Ai:Providers:*:TimeoutSeconds.", message);
    }

    [Fact]
    public void ProviderTimeoutInsideProviderException_IsStillAModelTimeout()
    {
        var exception = new AiProviderException(
            "ollama",
            null,
            "generation failed",
            new TaskCanceledException("timeout", new TimeoutException()));

        Assert.StartsWith("Модель не ответила за 120 с", LessonGenerationFailure.Describe(exception, 120), StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderHttpError_KeepsTheStatusCode()
    {
        var exception = new AiProviderException("ollama", HttpStatusCode.NotFound, "model not found");

        var message = LessonGenerationFailure.Describe(exception, 120);

        Assert.StartsWith("Провайдер ИИ ответил ошибкой 404", message, StringComparison.Ordinal);
        Assert.Contains("model not found", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreachableProvider_IsReportedWithoutAStatusCode()
    {
        var exception = new AiProviderException("ollama", null, "connection refused");

        var message = LessonGenerationFailure.Describe(exception, 120);

        Assert.StartsWith("Провайдер ИИ недоступен", message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedAnswer_IsReportedAsAFormatProblem()
    {
        var message = LessonGenerationFailure.Describe(new JsonException("unexpected token"), 120);

        Assert.Equal("Модель вернула ответ не в ожидаемом формате.", message);
    }

    [Fact]
    public void UnknownFailure_KeepsTheUnderlyingMessage()
    {
        var exception = new InvalidOperationException("outer", new InvalidOperationException("внутренняя причина"));

        Assert.Equal("внутренняя причина", LessonGenerationFailure.Describe(exception, 120));
    }
}
