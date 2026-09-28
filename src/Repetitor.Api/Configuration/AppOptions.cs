namespace Repetitor.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "repetitor-api";
    public string Audience { get; set; } = "repetitor-clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
    public int ClockSkewSeconds { get; set; } = 30;
    public string ResetPasswordUrl { get; set; } = "http://localhost:5080/reset-password";
}

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string DefaultChatProvider { get; set; } = "openai";
    public string DefaultChatModel { get; set; } = "gpt-4o-mini";
    public string DefaultEmbeddingProvider { get; set; } = "openai";
    public string DefaultGradingModel { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.7;
    public double GradingTemperature { get; set; } = 0.2;
    public int MaxOutputTokens { get; set; } = 1200;
    public int ChatHistoryLimit { get; set; } = 24;
    public int EmbeddingDimensions { get; set; } = 1536;
    public int EmbeddingLocalDimensions { get; set; } = 1024;
    public int EmbeddingBatchSize { get; set; } = 64;
    public int MaxRagContextItems { get; set; } = 8;
    public double RagMinSimilarity { get; set; } = 0.25;
    public bool LogPrompts { get; set; }
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AiProviderOptions
{
    public string Kind { get; set; } = "openai";
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string ChatModel { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public string? TtsModel { get; set; }
    public string? SttModel { get; set; }
    public string? TtsVoice { get; set; }
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
    public int RequestsPerMinute { get; set; } = 120;
    public double InputCostPerMillionTokens { get; set; }
    public double OutputCostPerMillionTokens { get; set; }
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public string RootPath { get; set; } = "media";
    public long MaxUploadBytes { get; set; } = 15 * 1024 * 1024;
    public int RetentionDays { get; set; } = 30;
    public string[] AllowedAudioContentTypes { get; set; } =
    [
        "audio/mpeg", "audio/mp3", "audio/wav", "audio/x-wav", "audio/ogg",
        "audio/webm", "audio/aac", "audio/mp4", "audio/flac", "audio/m4a"
    ];
}

public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    public int DefaultDailyGoalXp { get; set; } = 50;
    public int MaxWordsPerImport { get; set; } = 300;
    public int MaxFreeAiCallsPerDay { get; set; } = 100;
    public int MaxDeckSize { get; set; } = 2000;
    public int NewCardsPerDay { get; set; } = 20;
    public double[] SimilarityThresholds { get; set; } = [0.3, 0.5, 0.7];
}
