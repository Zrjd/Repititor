using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.Ai;

public enum AiProviderKind
{
    OpenAiCompatible = 0,
    Ollama = 1
}

public enum AiFinishReason
{
    Stop = 0,
    Length = 1,
    ContentFilter = 2,
    ToolCalls = 3,
    Error = 4
}

public sealed record AiChatMessage(string Role, string Content, string? Name = null)
{
    public static AiChatMessage System(string content) => new("system", content);
    public static AiChatMessage User(string content) => new("user", content);
    public static AiChatMessage Assistant(string content) => new("assistant", content);
}

public sealed record AiChatRequest
{
    public required IReadOnlyList<AiChatMessage> Messages { get; init; }
    public string? Model { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public string? ResponseJsonSchemaName { get; init; }
    public bool? JsonMode { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }
    public double? FrequencyPenalty { get; init; }
    public double? PresencePenalty { get; init; }
    public string? Seed { get; init; }
}

public sealed record AiChatResult
{
    public required string Content { get; init; }
    public string Model { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public AiFinishReason FinishReason { get; init; } = AiFinishReason.Stop;
    public string? Refusal { get; init; }
}

public sealed record AiEmbeddingRequest
{
    public required IReadOnlyList<string> Inputs { get; init; }
    public string? Model { get; init; }
    public int? Dimensions { get; init; }
}

public sealed record AiEmbeddingResult
{
    public required IReadOnlyList<float[]> Vectors { get; init; }
    public required string Model { get; init; }
    public int Dimensions { get; init; }
    public int Tokens { get; init; }
}

public sealed record AiSpeechRequest
{
    public required string Text { get; init; }
    public string? Voice { get; init; }
    public string Format { get; init; } = "mp3";
    public double Speed { get; init; } = 1.0;
    public string? LanguageCode { get; init; }
}

public sealed record AiSpeechResult
{
    public required byte[] Audio { get; init; }
    public required string ContentType { get; init; }
    public string? Voice { get; init; }
    public int? DurationMs { get; init; }
}

public sealed record AiTranscriptionRequest
{
    public required byte[] Audio { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public string? LanguageCode { get; init; }
    public string? Prompt { get; init; }
    public bool TranslateToEnglish { get; init; }
}

public sealed record AiTranscriptionResult
{
    public required string Text { get; init; }
    public double? DurationSeconds { get; init; }
    public string? Language { get; init; }
    public IReadOnlyList<AiTranscriptSegment> Segments { get; init; } = [];
}

public sealed record AiTranscriptSegment(double Start, double End, string Text, double? Confidence);

public sealed record AiProviderDescriptor
{
    public required string Name { get; init; }
    public required AiProviderKind Kind { get; init; }
    public required bool Enabled { get; init; }
    public required bool Configured { get; init; }
    public string? ChatModel { get; init; }
    public string? EmbeddingModel { get; init; }
    public string? TtsModel { get; init; }
    public string? SttModel { get; init; }
    public bool SupportsStreaming { get; init; } = true;
    public bool SupportsJsonMode { get; init; }
    public bool SupportsVision { get; init; }
    public int EmbeddingDimensions { get; init; }
    public int RequestsPerMinute { get; init; }
}

public interface IChatCompletionClient
{
    string Name { get; }
    AiProviderKind Kind { get; }
    string ChatModel { get; }
    bool SupportsJsonMode { get; }
    Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(AiChatRequest request, CancellationToken ct = default);
}

public interface IEmbeddingClient
{
    string Name { get; }
    AiProviderKind Kind { get; }
    string EmbeddingModel { get; }
    int Dimensions { get; }
    Task<AiEmbeddingResult> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default);
}

public interface ISpeechSynthesisClient
{
    string Name { get; }
    string? SynthesisModel { get; }
    Task<AiSpeechResult> SynthesizeAsync(AiSpeechRequest request, CancellationToken ct = default);
}

public interface ISpeechRecognitionClient
{
    string Name { get; }
    string? RecognitionModel { get; }
    Task<AiTranscriptionResult> TranscribeAsync(AiTranscriptionRequest request, CancellationToken ct = default);
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(string provider, HttpStatusCode? status, string message, Exception? inner = null)
        : base(message, inner)
    {
        Provider = provider;
        StatusCode = status;
    }

    public string Provider { get; }
    public HttpStatusCode? StatusCode { get; }
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;
    public bool IsTransient => StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

internal static class AiHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HttpClient Create(AiProviderOptions options, string name)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Repetitor.Api/1.0");
        if (options.Kind.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        else if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        return client;
    }

    public static Uri Resolve(string baseUrl, params string[] segments)
    {
        var normalized = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return new Uri(new Uri(normalized), string.Join("/", segments.Select(s => s.Trim('/'))));
    }

    public static string Join(params string[] segments) => string.Join("/", segments.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static async Task RaiseForStatusAsync(HttpResponseMessage response, string provider, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var snippet = body.Length > 600 ? body[..600] : body;
        var status = response.StatusCode;
        if (status == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            throw new AiProviderException(provider, status,
                $"Rate limited by {provider} on {operation}. Retry after {retryAfter?.ToString("0") ?? "unknown"}s. {snippet}");
        }

        throw new AiProviderException(provider, status, $"{provider} {operation} failed with {(int)status}: {snippet}");
    }

    public static JsonObject ParseObject(string json)
    {
        var node = JsonNode.Parse(json);
        return node as JsonObject
               ?? throw new AiProviderException("parser", null, "Expected a JSON object in provider response");
    }
}

internal static class AiText
{
    public static string Truncate(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max];
    }

    public static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}

internal static class StreamExtensions
{
    public static async IAsyncEnumerable<string> AsEmpty([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
