using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record StoredMedia(Guid Id, string RelativePath, string ContentType, long SizeBytes, string Url);

public interface IMediaStorage
{
    Task<StoredMedia> SaveAsync(byte[] content, string extension, string contentType, Guid? userId, MediaKind kind, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default);
    Stream OpenRead(string relativePath);
    bool Exists(string relativePath);
    void Delete(string relativePath);
    void EnsureDirectory(string relativePath);
    string ContentTypeFor(string extension);
}

public sealed class FileSystemMediaStorage(
    IOptions<MediaOptions> options,
    IWebHostEnvironment env,
    IDbContextFactory<AppDbContext> dbFactory) : IMediaStorage
{
    private readonly MediaOptions _options = options.Value;
    private readonly string _root = Path.IsPathRooted(options.Value.RootPath)
        ? options.Value.RootPath
        : Path.Combine(env.ContentRootPath, options.Value.RootPath);

    public string UrlFor(string relativePath) => "/media/" + relativePath.Replace('\\', '/').TrimStart('/');

    public async Task<StoredMedia> SaveAsync(
        byte[] content,
        string extension,
        string contentType,
        Guid? userId,
        MediaKind kind,
        CancellationToken ct = default)
    {
        if (content.LongLength > _options.MaxUploadBytes)
        {
            throw new MediaTooLargeException(content.LongLength, _options.MaxUploadBytes);
        }

        if (!_options.AllowedAudioContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnsupportedMediaTypeException(contentType);
        }

        var now = DateTimeOffset.UtcNow;
        var relative = Path.Combine(
            now.ToString("yyyy"),
            now.ToString("MM"),
            $"{Guid.NewGuid():N}{NormalizeExtension(extension)}");

        EnsureDirectory(relative);
        var absolute = Path.Combine(_root, relative);
        await File.WriteAllBytesAsync(absolute, content, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new MediaAsset
        {
            UserId = userId,
            Kind = kind,
            StoragePath = relative.Replace('\\', '/'),
            ContentType = contentType,
            SizeBytes = content.LongLength,
            ExpiresAt = now.AddDays(_options.RetentionDays)
        };
        db.MediaAssets.Add(entity);
        await db.SaveChangesAsync(ct);

        return new StoredMedia(entity.Id, entity.StoragePath, contentType, content.LongLength, UrlFor(entity.StoragePath));
    }

    public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        var absolute = ResolveSafe(relativePath);
        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException("Media not found", relativePath);
        }

        return await File.ReadAllBytesAsync(absolute, ct);
    }

    public Stream OpenRead(string relativePath)
    {
        var absolute = ResolveSafe(relativePath);
        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException("Media not found", relativePath);
        }

        return File.OpenRead(absolute);
    }

    public bool Exists(string relativePath) => File.Exists(ResolveSafe(relativePath));

    public void Delete(string relativePath)
    {
        var absolute = ResolveSafe(relativePath);
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }
    }

    public void EnsureDirectory(string relativePath)
    {
        var absolute = ResolveSafe(relativePath);
        var dir = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".webm" => "audio/webm",
        ".aac" => "audio/aac",
        ".m4a" => "audio/mp4",
        ".flac" => "audio/flac",
        ".mp4" => "audio/mp4",
        _ => "application/octet-stream"
    };

    private string ResolveSafe(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootFull = Path.GetFullPath(_root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path escapes media root");
        }

        return full;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".bin";
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }
}

public sealed class MediaTooLargeException(long actual, long max)
    : Exception($"Media size {actual} bytes exceeds the limit of {max} bytes");

public sealed class UnsupportedMediaTypeException(string contentType)
    : Exception($"Content type '{contentType}' is not allowed");

public interface ISpeechService
{
    Task<StoredMedia> SynthesizeAsync(string text, string languageCode, string? voice, double speed, Guid? userId, CancellationToken ct = default);
    Task<AiTranscriptionResult> TranscribeAsync(byte[] audio, string fileName, string contentType, string languageCode, string? prompt, Guid? userId, CancellationToken ct = default);
}

public sealed class SpeechService(
    IAiGateway gateway,
    IMediaStorage storage,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<AiOptions> aiOptions) : ISpeechService
{
    private readonly AiOptions _options = aiOptions.Value;

    public async Task<StoredMedia> SynthesizeAsync(
        string text,
        string languageCode,
        string? voice,
        double speed,
        Guid? userId,
        CancellationToken ct = default)
    {
        text = TextNormalizer.Collapse(text);
        if (text.Length == 0)
        {
            throw new ArgumentException("Text is empty", nameof(text));
        }

        var provider = FindProvider(p => !string.IsNullOrWhiteSpace(p.Value.TtsModel));
        if (provider is null)
        {
            throw new AiProviderException("tts", null,
                "No AI provider with TtsModel is configured. Set Ai:Providers:<name>:TtsModel.");
        }

        var client = gateway.ResolveSpeechSynthesis(provider)
                     ?? throw new AiProviderException(provider, null, "Provider has no speech synthesis capability");

        var result = await client.SynthesizeAsync(new AiSpeechRequest
        {
            Text = text,
            Voice = voice,
            Speed = speed <= 0 ? 1.0 : speed,
            LanguageCode = languageCode,
            Format = "mp3"
        }, ct);

        var stored = await storage.SaveAsync(result.Audio, ".mp3", result.ContentType, userId, MediaKind.GeneratedSpeech, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var asset = await db.MediaAssets.FirstAsync(a => a.Id == stored.Id, ct);
        asset.SourceText = text;
        asset.Provider = provider;
        asset.Model = client.SynthesisModel;
        await db.SaveChangesAsync(ct);

        return stored;
    }

    public async Task<AiTranscriptionResult> TranscribeAsync(
        byte[] audio,
        string fileName,
        string contentType,
        string languageCode,
        string? prompt,
        Guid? userId,
        CancellationToken ct = default)
    {
        var provider = FindProvider(p => !string.IsNullOrWhiteSpace(p.Value.SttModel));
        if (provider is null)
        {
            throw new AiProviderException("stt", null,
                "No AI provider with SttModel is configured. Set Ai:Providers:<name>:SttModel.");
        }

        var client = gateway.ResolveSpeechRecognition(provider)
                     ?? throw new AiProviderException(provider, null, "Provider has no speech recognition capability");

        return await client.TranscribeAsync(new AiTranscriptionRequest
        {
            Audio = audio,
            FileName = fileName,
            ContentType = contentType,
            LanguageCode = languageCode,
            Prompt = prompt
        }, ct);
    }

    private string? FindProvider(Func<KeyValuePair<string, AiProviderOptions>, bool> predicate) =>
        _options.Providers
            .Where(p => p.Value.Enabled && predicate(p))
            .Select(p => p.Key)
            .FirstOrDefault();
}
