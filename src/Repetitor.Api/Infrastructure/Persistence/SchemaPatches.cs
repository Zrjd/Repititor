using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Configuration;

namespace Repetitor.Api.Infrastructure.Persistence;

public sealed class SchemaPatches
{
    public const string PatchTable = "schema_patches";

    private readonly AppDbContext _db;
    private readonly AiOptions _ai;
    private readonly ILogger<SchemaPatches> _logger;

    public SchemaPatches(AppDbContext db, Microsoft.Extensions.Options.IOptions<AiOptions> ai, ILogger<SchemaPatches> logger)
    {
        _db = db;
        _ai = ai.Value;
        _logger = logger;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await EnsurePatchTableAsync(ct);
        await ApplyAsync("0001_pgvector_embeddings", ct);
        await ApplyAsync("0002_dictionary_trgm_search", ct);
        await ApplyAsync("0003_daily_stats_view", ct);
    }

    private async Task EnsurePatchTableAsync(CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync(
            $$"""
            CREATE TABLE IF NOT EXISTS {{PatchTable}} (
                id          text PRIMARY KEY,
                applied_at  timestamptz NOT NULL DEFAULT now()
            )
            """, ct);
    }

    private async Task ApplyAsync(string patchId, CancellationToken ct)
    {
        if (await IsAppliedAsync(patchId, ct))
        {
            return;
        }

        _logger.LogInformation("Applying schema patch {PatchId}", patchId);
        var sql = patchId switch
        {
            "0001_pgvector_embeddings" => VectorPatches(),
            "0002_dictionary_trgm_search" => TrgmPatches(),
            "0003_daily_stats_view" => ViewPatches(),
            _ => throw new InvalidOperationException($"Unknown patch {patchId}")
        };

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
        await _db.Database.ExecuteSqlRawAsync(
            $$"""INSERT INTO {{PatchTable}} (id) VALUES ({0}) ON CONFLICT DO NOTHING""",
            [patchId],
            ct);
    }

    private async Task<bool> IsAppliedAsync(string patchId, CancellationToken ct)
    {
        await using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM {PatchTable} WHERE id = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = patchId;
        cmd.Parameters.Add(p);

        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync(ct);
        }

        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private string VectorPatches()
    {
        var remote = _ai.EmbeddingDimensions;
        var local = _ai.EmbeddingLocalDimensions;

        return $$"""
        CREATE EXTENSION IF NOT EXISTS vector;
        CREATE EXTENSION IF NOT EXISTS pg_trgm;

        ALTER TABLE lexical_unit_embeddings
            ADD COLUMN IF NOT EXISTS embedding        vector({{remote}}),
            ADD COLUMN IF NOT EXISTS embedding_local  vector({{local}});

        CREATE INDEX IF NOT EXISTS ix_lue_embedding_hnsw
            ON lexical_unit_embeddings
            USING hnsw (embedding vector_cosine_ops)
            WITH (m = 16, ef_construction = 64);

        CREATE INDEX IF NOT EXISTS ix_lue_embedding_local_hnsw
            ON lexical_unit_embeddings
            USING hnsw (embedding_local vector_cosine_ops)
            WITH (m = 16, ef_construction = 64);

        """;
    }

    private static string TrgmPatches() => """
        CREATE INDEX IF NOT EXISTS ix_lexical_units_text_trgm
            ON lexical_units USING gin ("Text" gin_trgm_ops);

        CREATE INDEX IF NOT EXISTS ix_lexical_units_translation_trgm
            ON lexical_units USING gin ("Translation" gin_trgm_ops);
        """;

    private static string ViewPatches() => """
        CREATE OR REPLACE VIEW vw_user_learning_summary AS
        SELECT
            u."Id"                                   AS user_id,
            u."DisplayName"                          AS display_name,
            u."TotalXp"                              AS total_xp,
            u."CurrentStreak"                        AS current_streak,
            u."LongestStreak"                        AS longest_streak,
            (SELECT count(*) FROM user_lexical_units ul WHERE ul."UserId" = u."Id")                              AS words_total,
            (SELECT count(*) FROM user_lexical_units ul
              WHERE ul."UserId" = u."Id" AND ul."State" = 'Mastered')                                            AS words_mastered,
            (SELECT count(*) FROM review_cards rc
                JOIN user_lexical_units ul ON ul."Id" = rc."UserLexicalUnitId"
               WHERE ul."UserId" = u."Id" AND rc."DueAt" <= now())                                               AS cards_due,
            (SELECT count(*) FROM exercise_attempts ea
               WHERE ea."UserId" = u."Id" AND ea."CompletedAt" >= now() - interval '7 days')                    AS attempts_week
        FROM users u;
        """;
}
