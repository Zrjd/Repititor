using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Repetitor.Api.Configuration;

namespace Repetitor.Api.Infrastructure.Ai;

public sealed class OllamaClient(
    HttpClient http,
    string providerName,
    AiProviderOptions options,
    AiOptions aiOptions) : IOllamaClient
{
    /// <summary>
    /// Возвращает имя провайдера Ollama.
    /// Используется для идентификации провайдера в логах и при обработке ошибок.
    /// </summary>
    public string Name => providerName;
    /// <summary>
    /// Возвращает тип провайдера — Ollama.
    /// Нужен для определения, что используется локальный сервер Ollama.
    /// </summary>
    public AiProviderKind Kind => AiProviderKind.Ollama;
    /// <summary>
    /// Возвращает модель чата по умолчанию для Ollama.
    /// Если в конфигурации не указана модель, используется "qwen2.5:7b-instruct".
    /// </summary>
    public string ChatModel => string.IsNullOrWhiteSpace(options.ChatModel) ? "qwen2.5:7b-instruct" : options.ChatModel;
    /// <summary>
    /// Возвращает модель для создания эмбеддингов в Ollama.
    /// Если в конфигурации не указана модель, используется "mxbai-embed-large".
    /// </summary>
    public string EmbeddingModel => string.IsNullOrWhiteSpace(options.EmbeddingModel) ? "mxbai-embed-large" : options.EmbeddingModel;
    /// <summary>
    /// Указывает, что Ollama поддерживает режим JSON-ответа.
    /// В этом режиме модель возвращает валидный JSON.
    /// </summary>
    public bool SupportsJsonMode => true;

    int IEmbeddingClient.Dimensions => aiOptions.EmbeddingLocalDimensions;

    private HttpClient Http { get; } = http;

    /// <summary>
    /// Отправляет запрос к модели чата Ollama и возвращает полный ответ.
    /// Используется для получения результата одним блоком без стриминга.
    /// </summary>
    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var payload = BuildChatPayload(request, stream: false);
        using var response = await Http.PostAsJsonAsync(AiHttp.Resolve(options.BaseUrl, "api/chat"), payload, AiHttp.Json, ct);
        await AiHttp.RaiseForStatusAsync(response, providerName, "api/chat", ct);

        var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
        return new AiChatResult
        {
            Content = root["message"]?["content"]?.GetValue<string>() ?? string.Empty,
            Model = root["model"]?.GetValue<string>() ?? ChatModel,
            InputTokens = root["prompt_eval_count"]?.GetValue<int>() ?? 0,
            OutputTokens = root["eval_count"]?.GetValue<int>() ?? 0,
            FinishReason = root["done_reason"]?.GetValue<string>() switch
            {
                "length" => AiFinishReason.Length,
                _ => AiFinishReason.Stop
            }
        };
    }

    /// <summary>
    /// Отправляет запрос к модели чата Ollama и возвращает ответ в виде потока токенов.
    /// Позволяет отображать ответ по мере его генерации в реальном времени.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = BuildChatPayload(request, stream: true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AiHttp.Resolve(options.BaseUrl, "api/chat"))
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

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string? chunk = null;
            try
            {
                chunk = JsonNode.Parse(line)?["message"]?["content"]?.GetValue<string>();
            }
            catch (JsonException)
            {
            }

            if (!string.IsNullOrEmpty(chunk))
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// Создаёт эмбеддинги для списка текстов через API Ollama.
    /// При ошибке с несколькими входными данными автоматически пробует устаревший API.
    /// </summary>
    public async Task<AiEmbeddingResult> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? EmbeddingModel : request.Model;
        var inputs = request.Inputs.ToArray();

        try
        {
            var payload = new JsonObject
            {
                ["model"] = model,
                ["input"] = new JsonArray(inputs.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray())
            };

            using var response = await Http.PostAsJsonAsync(AiHttp.Resolve(options.BaseUrl, "api/embed"), payload, AiHttp.Json, ct);
            await AiHttp.RaiseForStatusAsync(response, providerName, "api/embed", ct);

            var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
            var embeddings = root["embeddings"]?.AsArray() ?? [];
            var vectors = embeddings
                .Where(e => e is not null)
                .Select(e => e!.AsArray().Select(x => (float)x!.GetValue<double>()).ToArray()!)
                .ToArray();

            return new AiEmbeddingResult
            {
                Vectors = vectors,
                Model = model,
                Dimensions = vectors.Length > 0 ? vectors[0].Length : aiOptions.EmbeddingLocalDimensions,
                Tokens = root["prompt_eval_count"]?.GetValue<int>() ?? 0
            };
        }
        catch (AiProviderException) when (inputs.Length == 1)
        {
            return await EmbedLegacyAsync(model, inputs[0], ct);
        }
    }

    private async Task<AiEmbeddingResult> EmbedLegacyAsync(string model, string input, CancellationToken ct)
    {
        var payload = new JsonObject { ["model"] = model, ["prompt"] = input };
        using var response = await Http.PostAsJsonAsync(AiHttp.Resolve(options.BaseUrl, "api/embeddings"), payload, AiHttp.Json, ct);
        await AiHttp.RaiseForStatusAsync(response, providerName, "api/embeddings", ct);

        var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
        var vector = (root["embedding"]?.AsArray() ?? []).Select(x => (float)x!.GetValue<double>()).ToArray();

        return new AiEmbeddingResult
        {
            Vectors = [vector],
            Model = model,
            Dimensions = vector.Length,
            Tokens = root["prompt_eval_count"]?.GetValue<int>() ?? 0
        };
    }

    /// <summary>
    /// Получает список доступных моделей с сервера Ollama.
    /// Используется для проверки, какие модели загружены на сервере.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(AiHttp.Resolve(options.BaseUrl, "api/tags"), ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var root = AiHttp.ParseObject(await response.Content.ReadAsStringAsync(ct));
        return (root["models"]?.AsArray() ?? [])
            .Select(m => m?["name"]?.GetValue<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToArray();
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
            ["stream"] = stream
        };

        var o = new JsonObject
        {
            ["temperature"] = request.Temperature ?? aiOptions.Temperature
        };

        if (request.MaxTokens is > 0)
        {
            o["num_predict"] = request.MaxTokens;
        }

        if (request.StopSequences is { Count: > 0 })
        {
            o["stop"] = new JsonArray(request.StopSequences.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        }

        if (request.FrequencyPenalty is { } fp)
        {
            o["frequency_penalty"] = fp;
        }

        if (request.PresencePenalty is { } pp)
        {
            o["presence_penalty"] = pp;
        }

        if (request.Seed is { } seed)
        {
            o["seed"] = seed;
        }

        payload["options"] = o;

        if (request.JsonMode == true)
        {
            payload["format"] = "json";
        }

        return payload;
    }
}

public interface IOllamaClient : IChatCompletionClient, IEmbeddingClient
{
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
