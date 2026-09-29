using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Repetitor.Api.Configuration;

namespace Repetitor.Api.Infrastructure.Ai;

public sealed class OpenAiCompatibleClient(
    HttpClient http,
    string providerName,
    AiProviderOptions options,
    AiOptions aiOptions) : IChatCompletionClient, IEmbeddingClient, ISpeechSynthesisClient, ISpeechRecognitionClient
{
    /// <summary>
    /// Возвращает имя провайдера AI (например, "openai" или "ollama").
    /// Используется для идентификации провайдера в логах и при обработке ошибок.
    /// </summary>
    public string Name => providerName;
    /// <summary>
    /// Возвращает тип провайдера — OpenAiCompatible.
    /// Нужен для определения, какой формат запросов использовать при работе с API.
    /// </summary>
    public AiProviderKind Kind => AiProviderKind.OpenAiCompatible;
    /// <summary>
    /// Возвращает модель чата по умолчанию для этого провайдера.
    /// Если в конфигурации не указана модель, используется глобальная модель из настроек AI.
    /// </summary>
    public string ChatModel => string.IsNullOrWhiteSpace(options.ChatModel) ? aiOptions.DefaultChatModel : options.ChatModel;
    /// <summary>
    /// Возвращает модель для создания эмбеддингов (векторных представлений текста).
    /// Эмбеддинги используются для семантического поиска и сравнения текстов.
    /// </summary>
    public string EmbeddingModel => options.EmbeddingModel;
    /// <summary>
    /// Возвращает модель синтеза речи (TTS) или null, если она не настроена.
    /// Используется для преобразования текста в аудио.
    /// </summary>
    public string? SynthesisModel => string.IsNullOrWhiteSpace(options.TtsModel) ? null : options.TtsModel;
    /// <summary>
    /// Возвращает модель распознавания речи (STT) или null, если она не настроена.
    /// Используется для преобразования аудио в текст.
    /// </summary>
    public string? RecognitionModel => string.IsNullOrWhiteSpace(options.SttModel) ? null : options.SttModel;
    /// <summary>
    /// Указывает, поддерживает ли провайдер режим JSON-ответа.
    /// В этом режиме модель гарантированно возвращает валидный JSON.
    /// </summary>
    public bool SupportsJsonMode => true;

    private int EmbeddingDimensions => providerName.Equals("ollama", StringComparison.OrdinalIgnoreCase)
        ? aiOptions.EmbeddingLocalDimensions
        : aiOptions.EmbeddingDimensions;

    int IEmbeddingClient.Dimensions => EmbeddingDimensions;

    private HttpClient Http { get; } = http;

    /// <summary>
    /// Отправляет запрос к модели чата и возвращает полный ответ.
    /// Используется для получения результата одним блоком, когда стриминг не нужен.
    /// </summary>
    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var payload = BuildChatPayload(request, stream: false);
        using var response = await Http.PostAsJsonAsync(AiHttp.Resolve(options.BaseUrl, "chat/completions"), payload, AiHttp.Json, ct);
        await AiHttp.RaiseForStatusAsync(response, providerName, "chat/completions", ct);

        var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
        var choice = root["choices"]?.AsArray().FirstOrDefault()?.AsObject();
        var message = choice?["message"]?.AsObject();
        var usage = root["usage"]?.AsObject();

        var content = message?["content"]?.GetValue<string>()
                      ?? string.Join("\n", message?["content"]?.AsArray().Select(n => n?["text"]?.GetValue<string>() ?? string.Empty) ?? []);

        return new AiChatResult
        {
            Content = content ?? string.Empty,
            Model = root["model"]?.GetValue<string>() ?? ChatModel,
            InputTokens = usage?["prompt_tokens"]?.GetValue<int>() ?? 0,
            OutputTokens = usage?["completion_tokens"]?.GetValue<int>() ?? 0,
            FinishReason = MapFinishReason(choice?["finish_reason"]?.GetValue<string>()),
            Refusal = message?["refusal"]?.GetValue<string>()
        };
    }

    /// <summary>
    /// Отправляет запрос к модели чата и возвращает ответ в виде потока токенов.
    /// Позволяет отображать ответ по мере его генерации, улучшая пользовательский опыт.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = BuildChatPayload(request, stream: true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AiHttp.Resolve(options.BaseUrl, "chat/completions"))
        {
            Content = JsonContent.Create(payload, options: AiHttp.Json)
        };

        using var response = await Http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new AiProviderException(providerName, response.StatusCode, $"{providerName} chat stream failed: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var idleTimeout = AiHttp.IdleTimeout(options.TimeoutSeconds);

        while (await AiHttp.ReadLineWithIdleTimeoutAsync(reader, idleTimeout, providerName, ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data is "]" or "[DONE]" or "")
            {
                if (data == "[DONE]")
                {
                    break;
                }

                continue;
            }

            string? delta = null;
            try
            {
                var node = JsonNode.Parse(data);
                delta = node?["choices"]?.AsArray().FirstOrDefault()?["delta"]?["content"]?.GetValue<string>();
            }
            catch (JsonException)
            {
            }

            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    /// <summary>
    /// Создаёт эмбеддинги (векторные представления) для списка текстов.
    /// Векторы используются для семантического поиска и сравнения текстов между собой.
    /// </summary>
    public async Task<AiEmbeddingResult> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? EmbeddingModel : request.Model;
        var payload = new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray(request.Inputs.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray())
        };

        if (request.Dimensions is > 0)
        {
            payload["dimensions"] = request.Dimensions;
        }

        using var response = await Http.PostAsJsonAsync(AiHttp.Resolve(options.BaseUrl, "embeddings"), payload, AiHttp.Json, ct);
        await AiHttp.RaiseForStatusAsync(response, providerName, "embeddings", ct);

        var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
        var data = root["data"]?.AsArray() ?? [];
        var vectors = new List<float[]>(data.Count);
        foreach (var item in data)
        {
            var arr = item?["embedding"]?.AsArray();
            if (arr is null)
            {
                continue;
            }

            vectors.Add(arr.Select(x => (float)x!.GetValue<double>()).ToArray());
        }

        return new AiEmbeddingResult
        {
            Vectors = vectors,
            Model = root["model"]?.GetValue<string>() ?? model,
            Dimensions = vectors.Count > 0 ? vectors[0].Length : EmbeddingDimensions,
            Tokens = root["usage"]?["total_tokens"]?.GetValue<int>() ?? 0
        };
    }

    /// <summary>
    /// Преобразует текст в речь (синтез речи) с помощью модели TTS.
    /// Возвращает аудиоданные в формате, указанном в запросе.
    /// </summary>
    public async Task<AiSpeechResult> SynthesizeAsync(AiSpeechRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.TtsModel))
        {
            throw new AiProviderException(providerName, null, $"Provider '{providerName}' has no TtsModel configured");
        }

        var payload = new JsonObject
        {
            ["model"] = options.TtsModel,
            ["input"] = request.Text,
            ["voice"] = request.Voice ?? options.TtsVoice ?? "alloy",
            ["response_format"] = request.Format,
            ["speed"] = AiText.Clamp(request.Speed, 0.25, 4.0)
        };

        using var response = await Http.PostAsJsonAsync(AiHttp.Resolve(options.BaseUrl, "audio/speech"), payload, AiHttp.Json, ct);
        await AiHttp.RaiseForStatusAsync(response, providerName, "audio/speech", ct);

        var audio = await response.Content.ReadAsByteArrayAsync(ct);
        return new AiSpeechResult
        {
            Audio = audio,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg",
            Voice = payload["voice"]?.GetValue<string>()
        };
    }

    /// <summary>
    /// Преобразует аудио в текст (распознавание речи) с помощью модели STT.
    /// Возвращает полный текст, длительность и сегменты с временными метками.
    /// </summary>
    public async Task<AiTranscriptionResult> TranscribeAsync(AiTranscriptionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.SttModel))
        {
            throw new AiProviderException(providerName, null, $"Provider '{providerName}' has no SttModel configured");
        }

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(request.Audio);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        form.Add(fileContent, "file", request.FileName);

        if (!string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            form.Add(new StringContent(request.LanguageCode), "language");
        }

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            form.Add(new StringContent(request.Prompt), "prompt");
        }

        form.Add(new StringContent("verbose_json"), "response_format");
        form.Add(new StringContent(options.SttModel), "model");
        if (request.TranslateToEnglish)
        {
            form.Add(new StringContent("true"), "translate");
        }

        using var response = await Http.PostAsync(AiHttp.Resolve(options.BaseUrl, "audio/transcriptions"), form, ct);
        await AiHttp.RaiseForStatusAsync(response, providerName, "audio/transcriptions", ct);

        var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
        var segments = new List<AiTranscriptSegment>();
        foreach (var s in root["segments"]?.AsArray() ?? [])
        {
            segments.Add(new AiTranscriptSegment(
                s?["start"]?.GetValue<double>() ?? 0,
                s?["end"]?.GetValue<double>() ?? 0,
                s?["text"]?.GetValue<string>() ?? string.Empty,
                s?["avg_logprob"]?.GetValue<double>() is { } lp ? Math.Exp(lp) : null));
        }

        return new AiTranscriptionResult
        {
            Text = root["text"]?.GetValue<string>() ?? string.Empty,
            DurationSeconds = root["duration"]?.GetValue<double>(),
            Language = root["language"]?.GetValue<string>() ?? request.LanguageCode,
            Segments = segments
        };
    }

    private JsonObject BuildChatPayload(AiChatRequest request, bool stream)
    {
        var messages = new JsonArray();
        foreach (var m in request.Messages)
        {
            var node = new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
            if (!string.IsNullOrWhiteSpace(m.Name))
            {
                node["name"] = m.Name;
            }

            messages.Add(node);
        }

        var payload = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? ChatModel : request.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature ?? aiOptions.Temperature,
            ["stream"] = stream
        };

        if (request.MaxTokens is > 0)
        {
            payload["max_tokens"] = request.MaxTokens;
        }

        if (request.FrequencyPenalty is { } fp)
        {
            payload["frequency_penalty"] = fp;
        }

        if (request.PresencePenalty is { } pp)
        {
            payload["presence_penalty"] = pp;
        }

        if (request.Seed is { } seed)
        {
            payload["seed"] = seed;
        }

        if (request.StopSequences is { Count: > 0 })
        {
            payload["stop"] = new JsonArray(request.StopSequences.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        }

        if (request.JsonMode == true)
        {
            payload["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        return payload;
    }

    private static AiFinishReason MapFinishReason(string? reason) => reason switch
    {
        "length" => AiFinishReason.Length,
        "content_filter" => AiFinishReason.ContentFilter,
        "tool_calls" or "function_call" => AiFinishReason.ToolCalls,
        _ => AiFinishReason.Stop
    };
}
