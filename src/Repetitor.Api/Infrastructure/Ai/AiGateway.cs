using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;
using System.Text.Json.Nodes;

namespace Repetitor.Api.Infrastructure.Ai;

public interface IAiGateway
{
    IReadOnlyList<AiProviderDescriptor> DescribeProviders();
    IChatCompletionClient ResolveChat(string? provider = null);
    IEmbeddingClient ResolveEmbedding(string? provider = null);
    ISpeechSynthesisClient? ResolveSpeechSynthesis(string? provider = null);
    ISpeechRecognitionClient? ResolveSpeechRecognition(string? provider = null);
    Task<AiChatResult> CompleteAsync(AiChatRequest request, AiOperation operation, Guid? userId, string? provider = null, CancellationToken ct = default);
    Task<JsonNode> CompleteJsonAsync(JsonNode schemaHint, string systemPrompt, string userPrompt, AiOperation operation, Guid? userId, string? provider = null, double? temperature = null, CancellationToken ct = default);
    Task<AiJsonResult> CompleteJsonWithUsageAsync(JsonNode schemaHint, string systemPrompt, string userPrompt, AiOperation operation, Guid? userId, string? provider = null, double? temperature = null, CancellationToken ct = default);
    Task<AiEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, string? provider = null, CancellationToken ct = default);
}

/// <summary>
/// Разобранный JSON-ответ модели вместе с числом токенов prompt'а и ответа.
/// </summary>
public sealed record AiJsonResult(JsonNode Json, int InputTokens, int OutputTokens);

public sealed class AiGateway : IAiGateway
{
    private readonly AiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AiGateway> _logger;
    private readonly Dictionary<string, Lazy<AiProviderBundle>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создаёт экземпляр AiGateway с необходимыми зависимостями.
    /// Инициализирует доступ к настройкам, HTTP-клиентам, базе данных и логгеру.
    /// </summary>
    public AiGateway(
        IOptions<AiOptions> options,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<AiGateway> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает описание всех включённых AI-провайдеров.
    /// Используется для отображения доступных провайдеров в административной панели.
    /// </summary>
    public IReadOnlyList<AiProviderDescriptor> DescribeProviders() =>
        _options.Providers
            .Where(p => p.Value.Enabled)
            .Select(p => Describe(p.Key, p.Value))
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.Name)
            .ToArray();

    /// <summary>
    /// Возвращает клиент для работы с чатом от указанного или провайдера по умолчанию.
    /// Выбрасывает исключение, если провайдер не поддерживает чат.
    /// </summary>
    public IChatCompletionClient ResolveChat(string? provider = null) => Get(provider ?? _options.DefaultChatProvider).Chat
        ?? throw new AiProviderException(provider ?? _options.DefaultChatProvider, null, "Provider has no chat capability");

    /// <summary>
    /// Возвращает клиент для создания эмбеддингов от указанного или провайдера по умолчанию.
    /// Выбрасывает исключение, если провайдер не поддерживает эмбеддинги.
    /// </summary>
    public IEmbeddingClient ResolveEmbedding(string? provider = null) => Get(provider ?? _options.DefaultEmbeddingProvider).Embedding
        ?? throw new AiProviderException(provider ?? _options.DefaultEmbeddingProvider, null, "Provider has no embedding capability");

    /// <summary>
    /// Возвращает клиент для синтеза речи или null, если провайдер не поддерживает TTS.
    /// Позволяет гибко настраивать, какие провайдеры могут использоваться для озвучки.
    /// </summary>
    public ISpeechSynthesisClient? ResolveSpeechSynthesis(string? provider = null)
    {
        var bundle = Get(provider ?? _options.DefaultChatProvider);
        return bundle.Speech;
    }

    /// <summary>
    /// Возвращает клиент для распознавания речи или null, если провайдер не поддерживает STT.
    /// Позволяет гибко настраивать, какие провайдеры могут использоваться для транскрибации.
    /// </summary>
    public ISpeechRecognitionClient? ResolveSpeechRecognition(string? provider = null)
    {
        var bundle = Get(provider ?? _options.DefaultChatProvider);
        return bundle.Recognition;
    }

    /// <summary>
    /// Выполняет запрос к модели чата с логированием и обработкой ошибок.
    /// Записывает информацию о вызове в базу данных для анализа стоимости и использования.
    /// </summary>
    public async Task<AiChatResult> CompleteAsync(
        AiChatRequest request,
        AiOperation operation,
        Guid? userId,
        string? provider = null,
        CancellationToken ct = default)
    {
        var client = ResolveChat(provider);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await client.CompleteAsync(request, ct);
            sw.Stop();
            await LogAsync(userId, client.Name, result.Model, operation, result.InputTokens, result.OutputTokens,
                sw.ElapsedMilliseconds, true, null, null, client, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var typed = ex as AiProviderException ?? new AiProviderException(client.Name, null, ex.Message, ex);
            await LogAsync(userId, client.Name, client.ChatModel, operation, 0, 0, sw.ElapsedMilliseconds, false,
                typed.StatusCode?.ToString(), ex.Message, client, ct);
            throw typed;
        }
    }

    /// <summary>
    /// Выполняет запрос к модели чата с требованием вернуть JSON по указанной схеме.
    /// Используется для структурированного ответа модели, например, при проверке заданий.
    /// </summary>
    public async Task<JsonNode> CompleteJsonAsync(
        JsonNode schemaHint,
        string systemPrompt,
        string userPrompt,
        AiOperation operation,
        Guid? userId,
        string? provider = null,
        double? temperature = null,
        CancellationToken ct = default)
    {
        var result = await CompleteJsonWithUsageAsync(
            schemaHint, systemPrompt, userPrompt, operation, userId, provider, temperature, ct);
        return result.Json;
    }

    /// <summary>
    /// Выполняет запрос к модели чата с требованием вернуть JSON по указанной схеме.
    /// В отличие от CompleteJsonAsync возвращает также число входных и выходных токенов,
    /// чтобы вызывающий код мог показать реальное потребление.
    /// </summary>
    public async Task<AiJsonResult> CompleteJsonWithUsageAsync(
        JsonNode schemaHint,
        string systemPrompt,
        string userPrompt,
        AiOperation operation,
        Guid? userId,
        string? provider = null,
        double? temperature = null,
        CancellationToken ct = default)
    {
        var prompt = BuildJsonPrompt(systemPrompt, schemaHint, userPrompt);
        var request = new AiChatRequest
        {
            Messages = [AiChatMessage.System(prompt)],
            Temperature = temperature ?? _options.GradingTemperature,
            MaxTokens = _options.MaxOutputTokens,
            JsonMode = true
        };

        var client = ResolveChat(provider);
        if (!client.SupportsJsonMode)
        {
            request = request with { JsonMode = null };
        }

        var result = await CompleteAsync(request, operation, userId, provider, ct);
        return new AiJsonResult(AiJson.Extract(result.Content), result.InputTokens, result.OutputTokens);
    }

    /// <summary>
    /// Создаёт эмбеддинги для списка текстов с логированием и обработкой ошибок.
    /// Записывает информацию о вызове в базу данных для анализа стоимости.
    /// </summary>
    public async Task<AiEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, string? provider = null, CancellationToken ct = default)
    {
        if (inputs.Count == 0)
        {
            return new AiEmbeddingResult { Vectors = [], Model = string.Empty, Dimensions = 0 };
        }

        var client = ResolveEmbedding(provider);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await client.EmbedAsync(new AiEmbeddingRequest { Inputs = inputs }, ct);
            sw.Stop();
            await LogAsync(null, client.Name, client.EmbeddingModel, AiOperation.Embedding, result.Tokens, 0,
                sw.ElapsedMilliseconds, true, null, null, null, ct);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var typed = ex as AiProviderException ?? new AiProviderException(client.Name, null, ex.Message, ex);
            await LogAsync(null, client.Name, client.EmbeddingModel, AiOperation.Embedding, 0, 0,
                sw.ElapsedMilliseconds, false, typed.StatusCode?.ToString(), ex.Message, null, ct);
            throw typed;
        }
    }

    private static string BuildJsonPrompt(string systemPrompt, JsonNode schemaHint, string userPrompt) =>
        $"""
         {systemPrompt}

         Answer with a single valid JSON document and nothing else. No markdown fences, no commentary.
         It must match this JSON shape:
         {schemaHint.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}
         """.Trim();

    private AiProviderBundle Get(string providerName)
    {
        if (!_options.Providers.TryGetValue(providerName, out var options))
        {
            throw new AiProviderException(providerName, null,
                $"Unknown AI provider '{providerName}'. Configured: {string.Join(", ", _options.Providers.Keys)}");
        }

        if (!options.Enabled)
        {
            throw new AiProviderException(providerName, null, $"AI provider '{providerName}' is disabled");
        }

        lock (_cache)
        {
            if (!_cache.TryGetValue(providerName, out var lazy))
            {
                lazy = new Lazy<AiProviderBundle>(() => Build(providerName, options), LazyThreadSafetyMode.ExecutionAndPublication);
                _cache[providerName] = lazy;
            }

            return lazy.Value;
        }
    }

    private AiProviderBundle Build(string providerName, AiProviderOptions options)
    {
        var isOllama = options.Kind.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        var http = _httpClientFactory.CreateClient(AiHttpClientNames.For(providerName, isOllama));

        if (isOllama)
        {
            var ollama = new OllamaClient(http, providerName, options, _options);
            return new AiProviderBundle(ollama, ollama, null, null);
        }

        var openAi = new OpenAiCompatibleClient(http, providerName, options, _options);
        return new AiProviderBundle(openAi, openAi, openAi, openAi);
    }

    private AiProviderDescriptor Describe(string name, AiProviderOptions options)
    {
        var isOllama = options.Kind.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        return new AiProviderDescriptor
        {
            Name = name,
            Kind = isOllama ? AiProviderKind.Ollama : AiProviderKind.OpenAiCompatible,
            Enabled = options.Enabled,
            Configured = isOllama || !string.IsNullOrWhiteSpace(options.ApiKey),
            ChatModel = options.ChatModel,
            EmbeddingModel = options.EmbeddingModel,
            TtsModel = options.TtsModel,
            SttModel = options.SttModel,
            SupportsJsonMode = true,
            EmbeddingDimensions = isOllama ? _options.EmbeddingLocalDimensions : _options.EmbeddingDimensions,
            RequestsPerMinute = options.RequestsPerMinute
        };
    }

    private async Task LogAsync(
        Guid? userId,
        string provider,
        string model,
        AiOperation operation,
        int inputTokens,
        int outputTokens,
        long latencyMs,
        bool success,
        string? errorCode,
        string? errorMessage,
        IChatCompletionClient? chatClient,
        CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var priceIn = 0d;
            var priceOut = 0d;
            if (_options.Providers.TryGetValue(provider, out var opts))
            {
                priceIn = opts.InputCostPerMillionTokens;
                priceOut = opts.OutputCostPerMillionTokens;
            }

            db.AiCallLogs.Add(new AiCallLog
            {
                UserId = userId,
                Provider = provider,
                Model = model,
                Operation = operation,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                LatencyMs = (int)Math.Min(latencyMs, int.MaxValue),
                Success = success,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage is null ? null : AiText.Truncate(errorMessage, 2000),
                EstimatedCostUsd = inputTokens / 1_000_000d * priceIn + outputTokens / 1_000_000d * priceOut
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist AI call log for {Provider}/{Operation}", provider, operation);
        }

        if (!success)
        {
            _logger.LogWarning("AI call failed: {Provider}/{Operation} {Error}", provider, operation, errorMessage);
        }
    }

    private sealed record AiProviderBundle(
        IChatCompletionClient Chat,
        IEmbeddingClient Embedding,
        ISpeechSynthesisClient? Speech,
        ISpeechRecognitionClient? Recognition);
}

public static class AiHttpClientNames
{
    /// <summary>
    /// Формирует уникальное имя HTTP-клиента для провайдера.
    /// Используется для разделения настроек и кэширования клиентов по типам провайдеров.
    /// </summary>
    public static string For(string providerName, bool isOllama) => $"ai:{(isOllama ? "ollama" : "openai")}:{providerName}";
}

public static class AiJson
{
    /// <summary>
    /// Извлекает JSON из ответа модели, обрабатывая различные форматы.
    /// Поддерживает чистый JSON, JSON в markdown-блоках и JSON с лишним текстом вокруг.
    /// </summary>
    public static JsonNode Extract(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            throw new AiProviderException("parser", null, "Model returned an empty response");
        }

        if (text[0] is '{' or '[')
        {
            try
            {
                return JsonNode.Parse(text)!;
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var body = text[(fenceStart + 3)..];
            var newline = body.IndexOf('\n');
            if (newline >= 0 && body[..newline].Trim().All(c => char.IsLetter(c) || c == '{'))
            {
                body = body[(newline + 1)..];
            }

            var fenceEnd = body.IndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                body = body[..fenceEnd];
            }

            try
            {
                return JsonNode.Parse(body.Trim())!;
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        var first = text.IndexOfAny(['{', '[']);
        var last = text.LastIndexOfAny(['}', ']']);
        if (first >= 0 && last > first)
        {
            try
            {
                return JsonNode.Parse(text[first..(last + 1)])!;
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        throw new AiProviderException("parser", null,
            "Model response did not contain valid JSON: " + AiText.Truncate(raw, 300));
    }
}
