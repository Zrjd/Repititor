using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;
using NpgsqlTypes;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record SemanticMatch(
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    string? PartOfSpeech,
    double Similarity,
    CefrLevel? MinLearnerLevel,
    string? ExampleTarget);

public sealed record ReindexReport(int Processed, int Created, int Failed);

public interface IEmbeddingService
{
    /// <summary>
    /// Определяет имя провайдера эмбеддингов по умолчанию, если конкретный не задан.
    /// </summary>
    string ResolveProvider(string? provider);
    /// <summary>
    /// Возвращает размерность векторов эмбеддингов для указанного провайдера.
    /// </summary>
    int DimensionsFor(string provider);
    /// <summary>
    /// Проверяет, является ли провайдер локальным (работает на собственных серверах).
    /// </summary>
    bool IsLocalProvider(string provider);
    /// <summary>
    /// Гарантирует, что для указанных лексических единиц есть актуальные эмбеддинги.
    /// </summary>
    Task EnsureEmbeddingsAsync(IReadOnlyCollection<Guid> lexicalUnitIds, string? provider, CancellationToken ct = default);
    /// <summary>
    /// Пересчитывает эмбеддинги для всех лексических единиц в базе.
    /// </summary>
    Task<ReindexReport> ReindexAsync(string? provider, int batchSize, int limit, bool force, CancellationToken ct = default);
}

public sealed class EmbeddingService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAiGateway gateway,
    IOptions<AiOptions> options,
    ILogger<EmbeddingService> logger) : IEmbeddingService
{
    private readonly AiOptions _options = options.Value;

    /// <summary>
    /// Возвращает имя провайдера эмбеддингов: переданное или значение по умолчанию из настроек.
    /// </summary>
    public string ResolveProvider(string? provider) => provider ?? _options.DefaultEmbeddingProvider;

    /// <summary>
    /// Возвращает размерность векторов для провайдера: локальные модели обычно используют меньшую размерность.
    /// </summary>
    public int DimensionsFor(string provider) => IsLocalProvider(provider)
        ? _options.EmbeddingLocalDimensions
        : _options.EmbeddingDimensions;

    /// <summary>
    /// Определяет, работает ли провайдер локально (например, Ollama).
    /// От этого зависит, в какое поле базы сохранять эмбеддинги.
    /// </summary>
    public bool IsLocalProvider(string provider) =>
        _options.Providers.TryGetValue(provider, out var p) &&
        p.Kind.Equals("ollama", StringComparison.OrdinalIgnoreCase);

    private static string ColumnFor(string provider) =>
        provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? "embedding_local" : "embedding";

    /// <summary>
    /// Проверяет и создаёт эмбеддинги для указанных лексических единиц при необходимости.
    /// Единицы с неизменённым содержимым пропускаются, чтобы не тратить ресурсы провайдера.
    /// </summary>
    public async Task EnsureEmbeddingsAsync(IReadOnlyCollection<Guid> lexicalUnitIds, string? provider, CancellationToken ct = default)
    {
        if (lexicalUnitIds.Count == 0)
        {
            return;
        }

        var providerName = ResolveProvider(provider);
        var client = gateway.ResolveEmbedding(providerName);
        var expected = DimensionsFor(providerName);
        var column = ColumnFor(providerName);
        var ids = lexicalUnitIds.ToArray();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var units = await db.LexicalUnits
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Text, u.Translation, u.ExampleTarget, u.ExampleNative, u.PartOfSpeech, u.ContentHash })
            .ToListAsync(ct);

        if (units.Count == 0)
        {
            return;
        }

        var existing = await db.LexicalUnitEmbeddings
            .Where(e => e.Provider == providerName && e.Model == client.EmbeddingModel && ids.Contains(e.LexicalUnitId))
            .Select(e => new EmbeddingStamp { Id = e.LexicalUnitId, Hash = e.ContentHash })
            .ToListAsync(ct);

        var fresh = new Dictionary<Guid, string?>(EqualityComparer<Guid>.Default);
        foreach (var row in existing)
        {
            fresh[row.Id] = row.Hash;
        }

        var pending = units
            .Where(u => !fresh.TryGetValue(u.Id, out var hash) || hash != (u.ContentHash ?? string.Empty))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var batchSize = Math.Max(1, _options.EmbeddingBatchSize);
        for (var offset = 0; offset < pending.Count; offset += batchSize)
        {
            var batch = pending.Skip(offset).Take(batchSize).ToList();
            var texts = batch.Select(u => BuildEmbeddingText(u.Text, u.Translation, u.ExampleTarget, u.ExampleNative, u.PartOfSpeech)).ToList();
            var result = await gateway.EmbedAsync(texts, providerName, ct);

            for (var i = 0; i < batch.Count && i < result.Vectors.Count; i++)
            {
                await UpsertAsync(db, batch[i].Id, providerName, client.EmbeddingModel, expected, column,
                    batch[i].ContentHash, result.Vectors[i], ct);
            }
        }
    }

    /// <summary>
    /// Пересчитывает эмбеддинги для лексических единиц порциями.
    /// При ошибке в одной порции пересчёт останавливается, а отчёт содержит число обработанных и неудачных единиц.
    /// </summary>
    public async Task<ReindexReport> ReindexAsync(string? provider, int batchSize, int limit, bool force, CancellationToken ct = default)
    {
        var providerName = ResolveProvider(provider);
        var client = gateway.ResolveEmbedding(providerName);
        var expected = DimensionsFor(providerName);
        var column = ColumnFor(providerName);

        var processed = 0;
        var failed = 0;
        batchSize = Math.Clamp(batchSize, 1, 256);
        limit = Math.Clamp(limit, 1, 200_000);

        while (processed < limit && !ct.IsCancellationRequested)
        {
            var take = Math.Min(batchSize, limit - processed);
            List<EmbeddingCandidate> units;

            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                IQueryable<Domain.Entities.LexicalUnit> query = db.LexicalUnits
                    .Where(u => u.Status != ContentStatus.Deprecated)
                    .OrderBy(u => u.CreatedAt)
                    .ThenBy(u => u.Id);

                if (!force)
                {
                    query = query.Where(u => !db.LexicalUnitEmbeddings
                        .Any(e => e.LexicalUnitId == u.Id && e.Provider == providerName && e.Model == client.EmbeddingModel));
                }
                else
                {
                    // Без сдвига каждая итерация брала бы одну и ту же порцию
                    // и пересчитывала одни и те же векторы до достижения limit.
                    query = query.Skip(processed);
                }

                units = await query
                    .Take(take)
                    .Select(u => new EmbeddingCandidate
                    {
                        Id = u.Id,
                        Text = u.Text,
                        Translation = u.Translation,
                        ExampleTarget = u.ExampleTarget,
                        ExampleNative = u.ExampleNative,
                        PartOfSpeech = u.PartOfSpeech,
                        Hash = u.ContentHash
                    })
                    .ToListAsync(ct);
            }

            if (units.Count == 0)
            {
                break;
            }

            try
            {
                var texts = units.Select(u => BuildEmbeddingText(u.Text, u.Translation, u.ExampleTarget, u.ExampleNative, u.PartOfSpeech)).ToList();
                var result = await gateway.EmbedAsync(texts, providerName, ct);

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                for (var i = 0; i < units.Count && i < result.Vectors.Count; i++)
                {
                    await UpsertAsync(db, units[i].Id, providerName, client.EmbeddingModel, expected, column,
                        units[i].Hash, result.Vectors[i], ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed += units.Count;
                logger.LogWarning(ex, "Embedding batch failed for provider {Provider}; stopping reindex", providerName);
                break;
            }

            processed += units.Count;
        }

        return new ReindexReport(processed, processed, failed);
    }

    private static async Task UpsertAsync(
        AppDbContext db,
        Guid lexicalUnitId,
        string provider,
        string model,
        int expected,
        string column,
        string? contentHash,
        float[] vector,
        CancellationToken ct)
    {
        var literal = ToVectorLiteral(vector, expected);
        var sql = $$"""
            INSERT INTO lexical_unit_embeddings
                ("Id", "LexicalUnitId", "Provider", "Model", "Dimensions", "ContentHash", "CreatedAt", "UpdatedAt", {{column}})
            VALUES (@id, @lexicalUnitId, @provider, @model, @dimensions, @contentHash, now(), now(), CAST(@vector AS vector))
            ON CONFLICT ("LexicalUnitId", "Provider", "Model") DO UPDATE
                SET {{column}} = EXCLUDED.{{column}},
                    "ContentHash" = EXCLUDED."ContentHash",
                    "Dimensions" = EXCLUDED."Dimensions",
                    "UpdatedAt" = now()
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter<Guid>("id", Guid.NewGuid()),
                new NpgsqlParameter<Guid>("lexicalUnitId", lexicalUnitId),
                new NpgsqlParameter<string>("provider", provider),
                new NpgsqlParameter<string>("model", model),
                new NpgsqlParameter<int>("dimensions", expected),
                new NpgsqlParameter<string>("contentHash", contentHash ?? string.Empty),
                new NpgsqlParameter<string>("vector", literal) { NpgsqlDbType = NpgsqlDbType.Text }
            ],
            ct);
    }

    internal static string ToVectorLiteral(float[] vector, int expected)
    {
        var sb = new StringBuilder(expected * 12 + 2);
        sb.Append('[');
        for (var i = 0; i < expected; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var value = i < vector.Length ? vector[i] : 0f;
            sb.Append(value.ToString("0.########", CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }

    internal static string BuildEmbeddingText(string text, string? translation, string? exampleTarget, string? exampleNative, PartOfSpeech pos)
    {
        var posText = pos == PartOfSpeech.Unknown ? string.Empty : pos.ToString().ToLowerInvariant();
        return string.Join("\n", new[]
        {
            text.Trim(),
            posText,
            translation?.Trim(),
            exampleTarget?.Trim(),
            exampleNative?.Trim()
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}

internal sealed class EmbeddingCandidate
{
    public Guid Id { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? Translation { get; init; }
    public string? ExampleTarget { get; init; }
    public string? ExampleNative { get; init; }
    public PartOfSpeech PartOfSpeech { get; init; }
    public string? Hash { get; init; }
}

internal sealed class EmbeddingStamp
{
    public Guid Id { get; init; }
    public string? Hash { get; init; }
}

public interface IVectorSearchService
{
    /// <summary>
    /// Ищет лексические единицы, близкие по смыслу к запросу.
    /// </summary>
    Task<IReadOnlyList<SemanticMatch>> SearchAsync(
        string query,
        Guid languageId,
        Guid translationLanguageId,
        string? provider,
        int limit,
        double minSimilarity,
        CefrFilter? levelFilter = null,
        Guid? excludeLexicalUnitId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Ищет лексические единицы, близкие по смыслу к указанным единицам.
    /// </summary>
    Task<IReadOnlyList<SemanticMatch>> SearchByIdsAsync(
        IReadOnlyList<Guid> lexicalUnitIds,
        string? provider,
        int perUnit,
        double minSimilarity,
        CancellationToken ct = default);
}

public sealed record CefrFilter(CefrLevel? MaxMinLevel = null);

public sealed class VectorSearchService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAiGateway gateway,
    IOptions<AiOptions> options) : IVectorSearchService
{
    private readonly AiOptions _options = options.Value;

    private static string ColumnFor(string provider) =>
        provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? "embedding_local" : "embedding";

    /// <summary>
    /// Выполняет семантический поиск лексических единиц по текстовому запросу.
    /// Запрос превращается в вектор, который сравнивается с сохранёнными эмбеддингами в базе.
    /// Поддерживаются фильтры по языку, уровню и исключение конкретной единицы.
    /// </summary>
    public async Task<IReadOnlyList<SemanticMatch>> SearchAsync(
        string query,
        Guid languageId,
        Guid translationLanguageId,
        string? provider,
        int limit,
        double minSimilarity,
        CefrFilter? levelFilter = null,
        Guid? excludeLexicalUnitId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var providerName = provider ?? _options.DefaultEmbeddingProvider;
        var column = ColumnFor(providerName);
        var client = gateway.ResolveEmbedding(providerName);
        var embedding = await gateway.EmbedAsync([query], providerName, ct);
        if (embedding.Vectors.Count == 0)
        {
            return [];
        }

        // Запрос приводится к той же размерности, что и сохранённые векторы:
        // колонка объявлена как vector(<EmbeddingDimensions>), а модель может вернуть меньше.
        // Добавленные нули не влияют на косинусное расстояние.
        var dimensions = client.Dimensions;
        var literal = EmbeddingService.ToVectorLiteral(embedding.Vectors[0], dimensions);
        var threshold = 1.0 - minSimilarity;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var cmd = await CreateCommandAsync(db, ct);
        cmd.CommandText = $"""
            WITH q AS (SELECT CAST(@vector AS vector) AS v)
            SELECT l."Id",
                   l."Text",
                   l."Translation",
                   l."Transcription",
                   l."PartOfSpeech"::text,
                   l."MinLearnerLevel"::text,
                   l."ExampleTarget",
                   1 - (e.{column} <=> q.v) AS similarity
            FROM lexical_unit_embeddings e
            CROSS JOIN q
            JOIN lexical_units l ON l."Id" = e."LexicalUnitId"
            WHERE e."Provider" = @provider
              AND e."Model" = @model
              AND e."Dimensions" = @dimensions
              AND e.{column} IS NOT NULL
              AND l."LanguageId" = @languageId
              AND l."TranslationLanguageId" = @translationLanguageId
              AND l."Status" <> 'Deprecated'
              AND (e.{column} <=> q.v) <= @threshold
              AND (@exclude_id::uuid IS NULL OR l."Id" <> @exclude_id)
              AND (@max_level::text IS NULL OR l."MinLearnerLevel"::text <= @max_level::text)
            ORDER BY e.{column} <=> q.v
            LIMIT @limit
            """;

        AddParam(cmd, "provider", providerName, NpgsqlDbType.Text);
        AddParam(cmd, "model", client.EmbeddingModel, NpgsqlDbType.Text);
        AddParam(cmd, "vector", literal, NpgsqlDbType.Text);
        AddParam(cmd, "dimensions", dimensions, NpgsqlDbType.Integer);
        AddParam(cmd, "languageId", languageId, NpgsqlDbType.Uuid);
        AddParam(cmd, "translationLanguageId", translationLanguageId, NpgsqlDbType.Uuid);
        AddParam(cmd, "exclude_id", excludeLexicalUnitId?.ToString(), NpgsqlDbType.Uuid);
        AddParam(cmd, "max_level", levelFilter?.MaxMinLevel?.ToString(), NpgsqlDbType.Text);
        AddParam(cmd, "threshold", threshold, NpgsqlDbType.Double);
        AddParam(cmd, "limit", Math.Clamp(limit, 1, 200), NpgsqlDbType.Integer);

        var results = new List<SemanticMatch>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SemanticMatch(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Math.Round(reader.GetDouble(7), 4),
                Enum.TryParse<CefrLevel>(reader.IsDBNull(5) ? null : reader.GetString(5), true, out var lvl) ? lvl : null,
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return results;
    }

    /// <summary>
    /// Ищет единицы, близкие по смыслу к заданным единицам, по нескольким соседям на каждую.
    /// Используется, например, для подбора дополнительной лексики по уже выбранным словам.
    /// </summary>
    public async Task<IReadOnlyList<SemanticMatch>> SearchByIdsAsync(
        IReadOnlyList<Guid> lexicalUnitIds,
        string? provider,
        int perUnit,
        double minSimilarity,
        CancellationToken ct = default)
    {
        if (lexicalUnitIds.Count == 0)
        {
            return [];
        }

        var providerName = provider ?? _options.DefaultEmbeddingProvider;
        var column = ColumnFor(providerName);
        var client = gateway.ResolveEmbedding(providerName);
        var threshold = 1.0 - minSimilarity;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var cmd = await CreateCommandAsync(db, ct);
        cmd.CommandText = $"""
            WITH src AS (
                SELECT "LexicalUnitId", {column} AS v
                  FROM lexical_unit_embeddings
                 WHERE "Provider" = @provider AND "Model" = @model
                   AND {column} IS NOT NULL
                   AND "LexicalUnitId" = ANY(@ids)
            )
            SELECT DISTINCT ON (src."LexicalUnitId")
                   other."LexicalUnitId" AS id,
                   l."Text",
                   l."Translation",
                   l."Transcription",
                   l."PartOfSpeech"::text,
                   l."MinLearnerLevel"::text,
                   l."ExampleTarget",
                   1 - (src.v <=> other.{column}) AS similarity
              FROM src
              JOIN lexical_unit_embeddings other
                ON other."Provider" = @provider
               AND other."Model" = @model
               AND other."Dimensions" = src."Dimensions"
               AND other.{column} IS NOT NULL
               AND NOT (other."LexicalUnitId" = ANY(@ids))
              JOIN lexical_units l ON l."Id" = other."LexicalUnitId"
             WHERE (src.v <=> other.{column}) <= @threshold
             ORDER BY src."LexicalUnitId", (src.v <=> other.{column})
            LIMIT @limit
            """;

        AddParam(cmd, "provider", providerName, NpgsqlDbType.Text);
        AddParam(cmd, "model", client.EmbeddingModel, NpgsqlDbType.Text);
        AddParam(cmd, "ids", lexicalUnitIds.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Uuid);
        AddParam(cmd, "threshold", threshold, NpgsqlDbType.Double);
        AddParam(cmd, "limit", Math.Max(lexicalUnitIds.Count * Math.Max(perUnit, 1), perUnit), NpgsqlDbType.Integer);

        var results = new List<SemanticMatch>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SemanticMatch(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Math.Round(reader.GetDouble(7), 4),
                Enum.TryParse<CefrLevel>(reader.IsDBNull(5) ? null : reader.GetString(5), true, out var lvl) ? lvl : null,
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return results;
    }

    internal static void AddParam(NpgsqlCommand cmd, string name, object? value, NpgsqlDbType type)
    {
        var p = new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };
        cmd.Parameters.Add(p);
    }

    private static async Task<NpgsqlCommand> CreateCommandAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return (NpgsqlCommand)connection.CreateCommand();
    }
}
