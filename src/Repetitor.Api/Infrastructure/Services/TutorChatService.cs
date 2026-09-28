using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Auth;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record TutorTurn(
    ChatMessage UserMessage,
    ChatMessage AssistantMessage,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    IReadOnlyList<SemanticMatch> RagContext);

public interface ITutorChatService
{
    Task<ChatSession> StartSessionAsync(Guid userId, TutorMode mode, CefrLevel? level, string? scenario, string? title, string? provider, string? model, bool useDictionary, CancellationToken ct = default);
    Task<TutorTurn> SendAsync(Guid userId, Guid sessionId, string message, string? provider, string? model, bool? useDictionary, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(Guid userId, Guid sessionId, string message, string? provider, string? model, bool? useDictionary, CancellationToken ct = default);
    Task<ChatMessage> RegenerateAsync(Guid userId, Guid sessionId, Guid messageId, CancellationToken ct = default);
    Task<int> RenameAsync(Guid userId, Guid sessionId, string title, CancellationToken ct = default);
}

public sealed class TutorChatService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAiGateway gateway,
    IVectorSearchService vectorSearch,
    IOptions<AiOptions> aiOptions,
    IClock clock) : ITutorChatService
{
    private readonly AiOptions _options = aiOptions.Value;

    /// <summary>
    /// Создаёт новую сессию чата с репетитором для пользователя.
    /// Определяет провайдера ИИ по умолчанию, формирует заголовок и сохраняет сессию в базу данных.
    /// </summary>
    public async Task<ChatSession> StartSessionAsync(
        Guid userId,
        TutorMode mode,
        CefrLevel? level,
        string? scenario,
        string? title,
        string? provider,
        string? model,
        bool useDictionary,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var client = gateway.ResolveChat(provider ?? user.PreferredAiProvider);
        var now = clock.UtcNow;

        var session = new ChatSession
        {
            UserId = userId,
            Title = TextNormalizer.Collapse(title ?? DefaultTitle(mode, scenario)),
            Mode = mode,
            Level = level ?? user.Level,
            Scenario = scenario is null ? null : AiText.Truncate(scenario, 2000),
            Provider = client.Name,
            Model = string.IsNullOrWhiteSpace(model) ? client.ChatModel : model,
            UseDictionaryContext = useDictionary,
            CreatedAt = now,
            LastMessageAt = now
        };

        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// Отправляет сообщение пользователя в рамках существующей сессии и возвращает ответ репетитора.
    /// Подготавливает контекст (история, RAG), вызывает ИИ и сохраняет оба сообщения в базу данных.
    /// </summary>
    public async Task<TutorTurn> SendAsync(
        Guid userId,
        Guid sessionId,
        string message,
        string? provider,
        string? model,
        bool? useDictionary,
        CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(userId, sessionId, message, provider, model, useDictionary, ct);

        var sw = Stopwatch.StartNew();
        var result = await gateway.CompleteAsync(prepared.Request, AiOperation.ChatCompletion, userId, prepared.ProviderName, ct);
        sw.Stop();

        var assistant = new ChatMessage
        {
            SessionId = sessionId,
            Role = ChatRole.Assistant,
            Content = result.Content,
            Provider = prepared.ProviderName,
            Model = result.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            RagContextRefs = prepared.Rag.Select(r => r.LexicalUnitId).ToArray()
        };

        await PersistAsync(userId, prepared, assistant, result.InputTokens, result.OutputTokens, ct);

        return new TutorTurn(prepared.UserMessage, assistant, result.InputTokens, result.OutputTokens,
            (int)sw.ElapsedMilliseconds, prepared.Rag);
    }

    /// <summary>
    /// Отправляет сообщение пользователя и возвращает ответ репетитора в виде потока текстовых фрагментов.
    /// Позволяет отображать ответ по мере его генерации, улучшая восприятие скорости работы.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        Guid userId,
        Guid sessionId,
        string message,
        string? provider,
        string? model,
        bool? useDictionary,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(userId, sessionId, message, provider, model, useDictionary, ct);
        var client = gateway.ResolveChat(prepared.ProviderName);
        var sw = Stopwatch.StartNew();
        var builder = new StringBuilder();

        await foreach (var chunk in client.StreamAsync(prepared.Request, ct))
        {
            builder.Append(chunk);
            yield return chunk;
        }

        sw.Stop();
        var assistant = new ChatMessage
        {
            SessionId = sessionId,
            Role = ChatRole.Assistant,
            Content = builder.ToString(),
            Provider = prepared.ProviderName,
            Model = string.IsNullOrWhiteSpace(model) ? client.ChatModel : model,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            RagContextRefs = prepared.Rag.Select(r => r.LexicalUnitId).ToArray()
        };

        await PersistAsync(userId, prepared, assistant, 0, 0, ct);
    }

    /// <summary>
    /// Перегенерирует ответ репетитора на конкретное сообщение, удаляя все последующие ответы.
    /// Полезно, когда пользователь хочет получить другой вариант ответа без создания новой сессии.
    /// </summary>
    public async Task<ChatMessage> RegenerateAsync(Guid userId, Guid sessionId, Guid messageId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
                      ?? throw new ChatSessionNotFoundException(sessionId);

        var target = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.SessionId == sessionId, ct)
                     ?? throw new ChatMessageNotFoundException(messageId);

        var userMessage = await db.ChatMessages
            .Where(m => m.SessionId == sessionId && m.Role == ChatRole.User && m.CreatedAt <= target.CreatedAt)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new ChatMessageNotFoundException(messageId);

        var toRemove = await db.ChatMessages
            .Where(m => m.SessionId == sessionId && m.Role == ChatRole.Assistant && m.CreatedAt >= target.CreatedAt)
            .ToListAsync(ct);
        db.ChatMessages.RemoveRange(toRemove);

        var (request, rag) = await BuildRequestAsync(db, session, userMessage.Content, session.Provider, session.Model, session.UseDictionaryContext, ct);
        var result = await gateway.CompleteAsync(request, AiOperation.ChatCompletion, userId, session.Provider, ct);

        var assistant = new ChatMessage
        {
            SessionId = sessionId,
            Role = ChatRole.Assistant,
            Content = result.Content,
            Provider = session.Provider,
            Model = result.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            RagContextRefs = rag.Select(r => r.LexicalUnitId).ToArray()
        };
        db.ChatMessages.Add(assistant);
        session.TotalInputTokens += result.InputTokens;
        session.TotalOutputTokens += result.OutputTokens;
        session.LastMessageAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return assistant;
    }

    /// <summary>
    /// Переименовывает существующую сессию чата.
    /// Позволяет пользователю задать понятное название диалога для удобной навигации в истории.
    /// </summary>
    public async Task<int> RenameAsync(Guid userId, Guid sessionId, string title, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
                      ?? throw new ChatSessionNotFoundException(sessionId);
        session.Title = AiText.Truncate(TextNormalizer.Collapse(title), 200);
        await db.SaveChangesAsync(ct);
        return 1;
    }

    private async Task<PreparedTurn> PrepareAsync(
        Guid userId,
        Guid sessionId,
        string message,
        string? provider,
        string? model,
        bool? useDictionary,
        CancellationToken ct)
    {
        message = TextNormalizer.Collapse(message);
        if (message.Length == 0)
        {
            throw new ArgumentException("Message is empty", nameof(message));
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
                      ?? throw new ChatSessionNotFoundException(sessionId);

        var providerName = provider ?? session.Provider;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = provider ?? _options.DefaultChatProvider;
        }

        var (request, rag) = await BuildRequestAsync(db, session, message, providerName, model, useDictionary ?? session.UseDictionaryContext, ct);

        var userMessage = new ChatMessage
        {
            SessionId = sessionId,
            Role = ChatRole.User,
            Content = message
        };

        return new PreparedTurn(request, providerName, userMessage, rag);
    }

    private async Task<(AiChatRequest Request, IReadOnlyList<SemanticMatch> Rag)> BuildRequestAsync(
        AppDbContext db,
        ChatSession session,
        string message,
        string provider,
        string? model,
        bool useDictionary,
        CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == session.UserId, ct);
        var target = await db.Languages.FirstAsync(l => l.Id == user.TargetLanguageId, ct);
        var interfaceLanguage = await db.Languages.FirstAsync(l => l.Id == user.InterfaceLanguageId, ct);

        var system = session.SystemPromptOverride
                     ?? PromptTemplates.TutorSystem(user, target, interfaceLanguage, session.Mode, session.Scenario);

        var messages = new List<AiChatMessage> { AiChatMessage.System(system) };

        IReadOnlyList<SemanticMatch> rag = [];

        if (useDictionary)
        {
            rag = await BuildRagContextAsync(user, target, interfaceLanguage, message, provider, ct);
            if (rag.Count > 0)
            {
                messages.Add(AiChatMessage.System(BuildRagBlock(rag)));
            }
        }

        var history = await db.ChatMessages
            .Where(m => m.SessionId == session.Id && m.Role != ChatRole.System)
            .OrderByDescending(m => m.CreatedAt)
            .Take(_options.ChatHistoryLimit)
            .ToListAsync(ct);

        messages.AddRange(history
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Role == ChatRole.User
                ? AiChatMessage.User(m.Content)
                : AiChatMessage.Assistant(m.Content)));

        messages.Add(AiChatMessage.User(message));

        var request = new AiChatRequest
        {
            Messages = messages,
            Model = string.IsNullOrWhiteSpace(model) ? session.Model : model,
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxOutputTokens
        };

        return (request, rag);
    }

    private async Task<IReadOnlyList<SemanticMatch>> BuildRagContextAsync(
        User user,
        Language target,
        Language interfaceLanguage,
        string message,
        string provider,
        CancellationToken ct)
    {
        try
        {
            return await vectorSearch.SearchAsync(
                message,
                target.Id,
                interfaceLanguage.Id,
                provider,
                _options.MaxRagContextItems,
                _options.RagMinSimilarity,
                new CefrFilter(user.Level),
                ct: ct);
        }
        catch (AiProviderException)
        {
            return [];
        }
    }

    private async Task PersistAsync(Guid userId, PreparedTurn prepared, ChatMessage assistant, int inputTokens, int outputTokens, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ChatMessages.Add(prepared.UserMessage);
        db.ChatMessages.Add(assistant);

        var session = await db.ChatSessions.FirstAsync(s => s.Id == assistant.SessionId, ct);
        session.Title = string.IsNullOrWhiteSpace(session.Title) || session.Title == DefaultTitle(session.Mode, session.Scenario)
            ? await AutoTitleAsync(prepared.UserMessage.Content, session.Provider, session.Model, ct)
            : session.Title;
        session.TotalInputTokens += inputTokens;
        session.TotalOutputTokens += outputTokens;
        session.LastMessageAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> AutoTitleAsync(string message, string provider, string? model, CancellationToken ct)
    {
        try
        {
            var result = await gateway.CompleteAsync(new AiChatRequest
            {
                Messages = [AiChatMessage.User(PromptTemplates.ChatTitlePrompt(message))],
                Model = model,
                Temperature = 0.2,
                MaxTokens = 24
            }, AiOperation.ChatCompletion, null, provider, ct);

            var title = TextNormalizer.Collapse(result.Content).Trim('"', '\'', ' ', '*', '#', '.');
            return AiText.Truncate(title.Length > 0 ? title : "Новый диалог", 200);
        }
        catch (Exception ex) when (ex is AiProviderException or OperationCanceledException)
        {
            return "Новый диалог";
        }
    }

    private static string BuildRagBlock(IReadOnlyList<SemanticMatch> matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Dictionary context (may be relevant, ignore if not):");
        foreach (var m in matches)
        {
            sb.Append("- ").Append(m.Text);
            if (!string.IsNullOrWhiteSpace(m.Translation))
            {
                sb.Append(" = ").Append(m.Translation);
            }

            if (!string.IsNullOrWhiteSpace(m.PartOfSpeech))
            {
                sb.Append(" (").Append(m.PartOfSpeech.ToLowerInvariant()).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(m.ExampleTarget))
            {
                sb.Append(" e.g. ").Append(m.ExampleTarget);
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string DefaultTitle(TutorMode mode, string? scenario) => mode switch
    {
        TutorMode.LessonRoleplay => scenario is null ? "Ролевая игра" : $"Ролевая игра: {TextNormalizer.Collapse(scenario)}",
        TutorMode.ExamPreparation => "Подготовка к экзамену",
        TutorMode.GrammarHelp => "Вопросы по грамматике",
        TutorMode.Interview => "Собеседование",
        TutorMode.Travel => "Путешествие",
        _ => "Свободная практика"
    };

    private sealed record PreparedTurn(
        AiChatRequest Request,
        string ProviderName,
        ChatMessage UserMessage,
        IReadOnlyList<SemanticMatch> Rag);
}

public sealed class ChatSessionNotFoundException(Guid id) : Exception($"Chat session {id} not found");

public sealed class ChatMessageNotFoundException(Guid id) : Exception($"Chat message {id} not found");
