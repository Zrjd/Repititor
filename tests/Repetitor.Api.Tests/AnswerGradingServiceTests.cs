using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Tests;

public sealed class AnswerGradingServiceTests
{
    private sealed class ThrowingGateway : IAiGateway
    {
        public IReadOnlyList<AiProviderDescriptor> DescribeProviders() => [];

        public IChatCompletionClient ResolveChat(string? provider = null) =>
            throw new AiProviderException("test", null, "provider unavailable");

        public IEmbeddingClient ResolveEmbedding(string? provider = null) =>
            throw new AiProviderException("test", null, "provider unavailable");

        public ISpeechSynthesisClient? ResolveSpeechSynthesis(string? provider = null) => null;

        public ISpeechRecognitionClient? ResolveSpeechRecognition(string? provider = null) => null;

        public Task<AiChatResult> CompleteAsync(AiChatRequest request, AiOperation operation, Guid? userId,
            string? provider = null, CancellationToken ct = default) => throw new AiProviderException("test", null, "provider unavailable");

        public Task<JsonNode> CompleteJsonAsync(JsonNode schemaHint, string systemPrompt, string userPrompt,
            AiOperation operation, Guid? userId, string? provider = null, double? temperature = null,
            CancellationToken ct = default) => throw new AiProviderException("test", null, "provider unavailable");

        public Task<AiJsonResult> CompleteJsonWithUsageAsync(JsonNode schemaHint, string systemPrompt, string userPrompt,
            AiOperation operation, Guid? userId, string? provider = null, double? temperature = null,
            CancellationToken ct = default) => throw new AiProviderException("test", null, "provider unavailable");

        public Task<AiEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, string? provider = null,
            CancellationToken ct = default) => throw new AiProviderException("test", null, "provider unavailable");
    }

    private static GradingRequest Request(string answer, string reference, params string[] variants) => new(
        answer,
        reference,
        variants,
        "en",
        "ru",
        CefrLevel.A1,
        "translate",
        UseAi: false);

    [Fact]
    public async Task GradeAsync_ExactMatchWithoutAi_IsCorrect()
    {
        var service = new AnswerGradingService(new ThrowingGateway());

        var result = await service.GradeAsync(Request("книга", "книга"), null, CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal(100, result.ScorePercent);
        Assert.Equal("heuristic", result.Method);
    }

    [Fact]
    public async Task GradeAsync_IgnoresCasingAndPunctuation()
    {
        var service = new AnswerGradingService(new ThrowingGateway());

        var result = await service.GradeAsync(Request("  Книга! ", "книга"), null, CancellationToken.None);

        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task GradeAsync_AcceptsDeclaredVariants()
    {
        var service = new AnswerGradingService(new ThrowingGateway());

        var result = await service.GradeAsync(
            Request("tomato", "the tomato", "tomato"),
            null,
            CancellationToken.None);

        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task GradeAsync_EmptyAnswer_ReturnsZeroScore()
    {
        var service = new AnswerGradingService(new ThrowingGateway());

        var result = await service.GradeAsync(Request("   ", "книга"), null, CancellationToken.None);

        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.ScorePercent);
        Assert.Equal("empty", result.Method);
        Assert.Contains("empty_answer", result.Issues);
    }

    [Fact]
    public async Task GradeAsync_WrongAnswer_ReportsExpectedValue()
    {
        var service = new AnswerGradingService(new ThrowingGateway());

        var result = await service.GradeAsync(Request("яблоко", "книга"), null, CancellationToken.None);

        Assert.False(result.IsCorrect);
        Assert.True(result.ScorePercent < 50);
        Assert.Equal("книга", result.CorrectedAnswer);
        Assert.NotEmpty(result.Hints);
    }

    [Fact]
    public async Task GradeAsync_WhenAiFails_FallsBackToHeuristic()
    {
        var service = new AnswerGradingService(new ThrowingGateway());
        var request = Request("книга", "книга") with { UseAi = true };

        var result = await service.GradeAsync(request, null, CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal("heuristic", result.Method);
    }

    [Fact]
    public async Task GradeExerciseAsync_WithoutPayload_ReturnsInvalid()
    {
        var service = new AnswerGradingService(new ThrowingGateway());
        var exercise = new Domain.Entities.Exercise
        {
            LanguageId = Guid.NewGuid(),
            Type = ExerciseType.MultipleChoice,
            Title = "empty",
            Instructions = "-",
            Payload = null
        };

        var result = await service.GradeExerciseAsync(
            exercise,
            new JsonObject(),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsCorrect);
        Assert.Equal("invalid", result.Method);
    }

    [Fact]
    public async Task GradeExerciseAsync_MultipleChoice_CountsCorrectAnswers()
    {
        var service = new AnswerGradingService(new ThrowingGateway());
        var exercise = new Domain.Entities.Exercise
        {
            LanguageId = Guid.NewGuid(),
            Type = ExerciseType.MultipleChoice,
            Title = "colors",
            Instructions = "choose",
            Payload = new JsonObject
            {
                ["items"] = new JsonArray(
                    Item("q1", 0),
                    Item("q2", 1))
            }
        };

        var answers = new JsonObject
        {
            ["q1"] = 0,
            ["q2"] = 0
        };

        var result = await service.GradeExerciseAsync(
            exercise, answers, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(50, result.ScorePercent);
        Assert.Equal("rule_based", result.Method);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GradeExerciseAsync_MultipleChoice_AcceptsOptionText()
    {
        var service = new AnswerGradingService(new ThrowingGateway());
        var exercise = new Domain.Entities.Exercise
        {
            LanguageId = Guid.NewGuid(),
            Type = ExerciseType.MultipleChoice,
            Title = "colors",
            Instructions = "choose",
            Payload = new JsonObject
            {
                ["items"] = new JsonArray(
                    Item("q1", 0),
                    Item("q2", 1))
            }
        };

        var answers = new JsonObject
        {
            ["q1"] = "alpha",
            ["q2"] = " BETA "
        };

        var result = await service.GradeExerciseAsync(
            exercise, answers, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(100, result.ScorePercent);
        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task GradeExerciseAsync_MultipleChoice_UnwrapsNestedAnswers()
    {
        var service = new AnswerGradingService(new ThrowingGateway());
        var exercise = new Domain.Entities.Exercise
        {
            LanguageId = Guid.NewGuid(),
            Type = ExerciseType.MultipleChoice,
            Title = "colors",
            Instructions = "choose",
            Payload = new JsonObject
            {
                ["items"] = new JsonArray(Item("q1", 1))
            }
        };

        var answers = new JsonObject
        {
            ["answers"] = new JsonObject { ["q1"] = 1 }
        };

        var result = await service.GradeExerciseAsync(
            exercise, answers, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(100, result.ScorePercent);
    }

    [Fact]
    public async Task GradeExerciseAsync_GapFill_NormalizesAnswers()
    {
        var service = new AnswerGradingService(new ThrowingGateway());
        var exercise = new Domain.Entities.Exercise
        {
            LanguageId = Guid.NewGuid(),
            Type = ExerciseType.GapFill,
            Title = "verbs",
            Instructions = "fill",
            Payload = new JsonObject
            {
                ["items"] = new JsonArray(new JsonObject
                {
                    ["id"] = "g1",
                    ["text"] = "I ___ water.",
                    ["correct_answer"] = "drink",
                    ["accepted_variants"] = new JsonArray("drinks", "drinking")
                })
            }
        };

        var answers = new JsonObject
        {
            ["g1"] = "Drink"
        };

        var result = await service.GradeExerciseAsync(
            exercise, answers, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal(100, result.ScorePercent);
    }

    private static JsonObject Item(string id, int correctIndex) => new()
    {
        ["id"] = id,
        ["options"] = new JsonArray("alpha", "beta", "gamma"),
        ["correct_index"] = correctIndex
    };
}
