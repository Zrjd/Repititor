using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Infrastructure.Ai;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/speech")]
[Authorize]
public sealed class SpeechController(
    ISpeechService speech,
    IPronunciationService pronunciation,
    IMediaStorage storage,
    IMediaDbService media,
    IUserDbService users,
    ICatalogDbService catalog,
    ILexiconDbService lexicon,
    IProgressService progress,
    IOptions<MediaOptions> mediaOptions) : ControllerBase
{
    /// <summary>
    /// Синтезирует речь из текста (Text-to-Speech).
    /// Преобразует переданный текст в аудиофайл с выбранным голосом и скоростью.
    /// Возвращает URL для воспроизведения и метаданные аудио (тип, размер).
    /// Используется для озвучки слов и фраз при обучении.
    /// </summary>
    [HttpPost("synthesize")]
    [ProducesResponseType(typeof(SynthesizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SynthesizeResponse>> Synthesize(SynthesizeRequest request, CancellationToken ct)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageCode = await ResolveLanguageAsync(request.Language, user, ct);
        var stored = await speech.SynthesizeAsync(request.Text, languageCode, request.Voice, request.Speed, user.Id, ct);

        return Ok(new SynthesizeResponse(stored.Url, stored.Id, stored.ContentType, stored.SizeBytes, "tts"));
    }

    /// <summary>
    /// Распознаёт речь из аудиофайла (Speech-to-Text).
    /// Принимает аудиозапись, определяет язык и преобразует речь в текст.
    /// Возвращает полный текст, длительность, сегменты с временными метками
    /// и уверенностью распознавания. Также сохраняет аудиофайл в хранилище.
    /// </summary>
    [HttpPost("transcribe")]
    [ProducesResponseType(typeof(TranscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<TranscriptionResponse>> Transcribe(TranscribeRequest request, CancellationToken ct)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var file = request.Audio;
        if (file.Length <= 0)
        {
            return BadRequest(new ErrorResponse("empty_audio", "Аудиофайл пуст."));
        }

        if (file.Length > mediaOptions.Value.MaxUploadBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new ErrorResponse("file_too_large", $"Максимальный размер файла: {mediaOptions.Value.MaxUploadBytes} байт."));
        }

        var contentType = file.ContentType ?? "application/octet-stream";
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var languageCode = await ResolveLanguageAsync(request.Language, user, ct);
        var result = await speech.TranscribeAsync(bytes, file.FileName, contentType, languageCode, request.Prompt, user.Id, ct);

        var stored = await storage.SaveAsync(bytes, Path.GetExtension(file.FileName), contentType, user.Id, MediaKind.UserRecording, ct);

        return Ok(new TranscriptionResponse(
            result.Text,
            result.Language,
            result.DurationSeconds,
            result.Segments.Select(s => new TranscriptSegmentResponse(s.Start, s.End, s.Text, s.Confidence)).ToArray(),
            stored.Url));
    }

    /// <summary>
    /// Оценивает произношение пользователя по аудиозаписи.
    /// Сравнивает распознанную речь с целевым текстом и вычисляет оценки
    /// по нескольким критериям: точность, беглость, полнота, просодия.
    /// Сохраняет попытку в базу, начисляет XP и возвращает детальный разбор
    /// с оценками на уровне отдельных слов и подсказками.
    /// </summary>
    [HttpPost("pronunciation")]
    [ProducesResponseType(typeof(PronunciationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PronunciationResponse>> Assess(PronunciationAssessmentRequest request, CancellationToken ct)
    {
        var user = await LoadUserAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var languageCode = await ResolveLanguageAsync(request.Language, user, ct);
        var file = request.Audio;

        if (file.Length > mediaOptions.Value.MaxUploadBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new ErrorResponse("file_too_large", "Файл слишком большой."));
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var audio = buffer.ToArray();
        var contentType = file.ContentType ?? "audio/wav";

        StoredMedia stored;
        AiTranscriptionResult transcription;

        try
        {
            stored = await storage.SaveAsync(audio, Path.GetExtension(file.FileName), contentType, user.Id, MediaKind.PronunciationSample, ct);
        }
        catch (UnsupportedMediaTypeException ex)
        {
            return BadRequest(new ErrorResponse("unsupported_media_type", ex.Message));
        }

        try
        {
            transcription = await speech.TranscribeAsync(audio, file.FileName, contentType, languageCode, request.TargetText, user.Id, ct);
        }
        catch (AiProviderException ex)
        {
            if (string.IsNullOrWhiteSpace(request.Transcript))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse("stt_unavailable", $"Распознавание речи недоступно: {ex.Message}"));
            }

            transcription = new AiTranscriptionResult { Text = request.Transcript!, Language = languageCode };
            stored = new StoredMedia(Guid.Empty, string.Empty, contentType, audio.LongLength, string.Empty);
        }

        string? transcriptionHint = null;
        if (request.LexicalUnitId is { } lexicalUnitId)
        {
            var unit = await lexicon.FindUnitAsync(lexicalUnitId, ct);
            transcriptionHint = unit?.Transcription;
        }

        var result = await pronunciation.AssessAsync(new PronunciationRequest(
            request.TargetText,
            transcription.Text,
            languageCode,
            transcription.Segments,
            transcriptionHint,
            request.Provider,
            request.UseAi), user.Id, ct);

        var xp = result.OverallScore / 5;
        var attempt = new Domain.Entities.PronunciationAttempt
        {
            UserId = user.Id,
            MediaAssetId = stored.Id == Guid.Empty ? null : stored.Id,
            LexicalUnitId = request.LexicalUnitId,
            TargetText = request.TargetText,
            Transcript = transcription.Text,
            RecognizedText = result.RecognizedText,
            OverallScore = result.OverallScore,
            AccuracyScore = result.AccuracyScore,
            FluencyScore = result.FluencyScore,
            CompletenessScore = result.CompletenessScore,
            ProsodyScore = result.ProsodyScore,
            WordLevelScores = new System.Text.Json.Nodes.JsonArray(
                result.Words.Select(w => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                {
                    ["word"] = w.Word,
                    ["expected"] = w.Expected,
                    ["recognized"] = w.Recognized,
                    ["score"] = w.Score,
                    ["hint"] = w.Hint
                }).ToArray()),
            FeedbackMarkdown = result.Feedback,
            Provider = request.Provider,
            Model = result.Method,
            XpEarned = xp
        };

        var saved = await media.AddPronunciationAttemptAsync(attempt, ct);

        await progress.RegisterActivityAsync(user.Id, xp, 0, 0, 0, 0, 0, 0, ct);

        return StatusCode(StatusCodes.Status201Created, new PronunciationResponse(
            saved.Id,
            saved.TargetText,
            saved.RecognizedText,
            saved.OverallScore,
            saved.AccuracyScore,
            saved.FluencyScore,
            saved.CompletenessScore,
            saved.ProsodyScore,
            result.Feedback,
            result.Words.Select(w => new WordScoreResponse(w.Word, w.Expected, w.Recognized, w.Score, w.Hint)).ToArray(),
            result.Method,
            stored.Url,
            xp,
            saved.CreatedAt));
    }

    /// <summary>
    /// Возвращает историю попыток оценки произношения текущего пользователя.
    /// Результат отсортирован по дате (новые сверху), количество ограничивается
    /// параметром limit (1–200). Используется для просмотра прогресса
    /// и повторного просмотра результатов предыдущих попыток.
    /// </summary>
    [HttpGet("pronunciation/attempts")]
    [ProducesResponseType(typeof(PronunciationResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<PronunciationResponse[]>> Attempts([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var attempts = await media.GetPronunciationAttemptsAsync(
            CurrentUserAccessor.GetUserId(User), Math.Clamp(limit, 1, 200), ct);

        return Ok(attempts.Select(a => new PronunciationResponse(
            a.Id, a.TargetText, a.RecognizedText, a.OverallScore, a.AccuracyScore, a.FluencyScore,
            a.CompletenessScore, a.ProsodyScore, a.FeedbackMarkdown ?? string.Empty, [], a.Model ?? "heuristic",
            null, a.XpEarned, a.CreatedAt)).ToArray());
    }

    /// <summary>
    /// Возвращает список аудиозаписей текущего пользователя.
    /// Включает все загруженные медиафайлы: записи произношения,
    /// распознавания речи и другие. Для каждой записи возвращается
    /// метаданные и временный URL для доступа к файлу.
    /// </summary>
    [HttpGet("recordings")]
    [ProducesResponseType(typeof(object[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<object[]>> Recordings([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var assets = await media.GetRecordingsAsync(CurrentUserAccessor.GetUserId(User), Math.Clamp(limit, 1, 200), ct);

        return Ok(assets
            .Select(a => (object)new
            {
                a.Id,
                a.Kind,
                a.ContentType,
                a.SizeBytes,
                a.DurationMs,
                a.SourceText,
                a.CreatedAt,
                a.ExpiresAt,
                a.Url
            })
            .ToArray());
    }

    /// <summary>
    /// Удаляет аудиозапись пользователя по идентификатору.
    /// Файл удаляется из хранилища и запись из базы данных.
    /// Доступна только владельцу записи — при попытке удалить чужую
    /// запись возвращается 404 Not Found.
    /// </summary>
    [HttpDelete("recordings/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRecording(Guid id, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var asset = await media.FindRecordingAsync(userId, id, ct);
        if (asset is null)
        {
            return NotFound();
        }

        storage.Delete(asset.StoragePath);
        await media.DeleteRecordingAsync(userId, id, ct);
        return NoContent();
    }

    private async Task<User?> LoadUserAsync(CancellationToken ct) =>
        await users.FindAsync(CurrentUserAccessor.GetUserId(User), ct);

    private async Task<string> ResolveLanguageAsync(string? requested, User user, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim().ToLowerInvariant();
        }

        return await catalog.ResolveLanguageCodeAsync(user.TargetLanguageId, ct) ?? "en";
    }
}
