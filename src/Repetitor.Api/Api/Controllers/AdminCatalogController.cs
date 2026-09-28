using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Teacher")]
public sealed class AdminCatalogController(
    IDbContextFactory<AppDbContext> dbFactory,
    IContentGenerationService contentGeneration,
    IAiGateway gateway,
    IOptions<AiOptions> aiOptions,
    IClock clock) : ControllerBase
{
    private const string AiSettingsKey = "ai.settings";

    /// <summary>
    /// Возвращает список всех курсов для панели управления, включая неопубликованные.
    /// Поддерживает фильтрацию по языку и возможность скрыть неопубликованные курсы.
    /// Включает количество уроков и дату создания для каждого курса.
    /// </summary>
    [HttpGet("courses")]
    [ProducesResponseType(typeof(AdminCourseResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCourseResponse[]>> Courses(
        [FromQuery] Guid? languageId, [FromQuery] bool includeUnpublished = true, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Courses.Include(c => c.Language).AsNoTracking().AsQueryable();

        if (languageId is { } lid)
        {
            query = query.Where(c => c.LanguageId == lid);
        }

        if (!includeUnpublished)
        {
            query = query.Where(c => c.IsPublished);
        }

        var courses = await query
            .OrderBy(c => c.Language!.SortOrder)
            .ThenBy(c => c.SortOrder)
            .ToListAsync(ct);

        return Ok(courses.Select(c => new AdminCourseResponse(
            c.Id, c.Slug, c.Title, c.Description, c.Level.ToString(), c.LanguageId, c.Language!.Code,
            c.CoverUrl, c.AccentColor, c.EstimatedMinutes, c.IsPublished, c.SortOrder,
            c.Lessons.Count, c.CreatedAt)).ToArray());
    }

    /// <summary>
    /// Создаёт новый курс в каталоге.
    /// Проверяет существование указанного языка перед созданием.
    /// Возвращает 201 с данными созданного курса и ссылкой на получение его деталей.
    /// </summary>
    [HttpPost("courses")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminCourseResponse>> CreateCourse(CreateCourseRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Languages.AnyAsync(l => l.Id == request.LanguageId, ct))
        {
            return BadRequest(new ErrorResponse("invalid_language", "Language not found"));
        }

        var course = new Course
        {
            Slug = request.Slug,
            Title = request.Title,
            Description = request.Description,
            Level = request.Level,
            LanguageId = request.LanguageId,
            CoverUrl = request.CoverUrl,
            AccentColor = request.AccentColor,
            EstimatedMinutes = request.EstimatedMinutes,
            IsPublished = request.IsPublished,
            SortOrder = request.SortOrder
        };

        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCourse), new { id = course.Id }, ToResponse(course));
    }

    /// <summary>
    /// Получает детальную информацию о курсе по идентификатору для редактирования в панели управления.
    /// Возвращает 404, если курс не найден.
    /// </summary>
    [HttpGet("courses/{id}")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCourseResponse>> GetCourse(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.Include(c => c.Language).FirstOrDefaultAsync(c => c.Id == id, ct);
        return course is null ? NotFound() : Ok(ToResponse(course));
    }

    /// <summary>
    /// Обновляет параметры существующего курса.
    /// Поддерживает частичное обновление — изменяются только переданные поля.
    /// При смене языка проверяет его существование. Возвращает 404, если курс не найден.
    /// </summary>
    [HttpPut("courses/{id}")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCourseResponse>> UpdateCourse(Guid id, UpdateCourseRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.Include(c => c.Language).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return NotFound();
        }

        if (request.Slug is not null) course.Slug = request.Slug;
        if (request.Title is not null) course.Title = request.Title;
        if (request.Description is not null) course.Description = request.Description;
        if (request.Level is { } level) course.Level = level;
        if (request.LanguageId is { } languageId)
        {
            if (!await db.Languages.AnyAsync(l => l.Id == languageId, ct))
            {
                return BadRequest(new ErrorResponse("invalid_language", "Language not found"));
            }
            course.LanguageId = languageId;
        }
        if (request.CoverUrl is not null) course.CoverUrl = request.CoverUrl;
        if (request.AccentColor is not null) course.AccentColor = request.AccentColor;
        if (request.EstimatedMinutes is { } minutes) course.EstimatedMinutes = minutes;
        if (request.IsPublished is { } published) course.IsPublished = published;
        if (request.SortOrder is { } order) course.SortOrder = order;

        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(course));
    }

    /// <summary>
    /// Удаляет курс из каталога вместе со связанными данными.
    /// Операция необратима. Возвращает 404, если курс не найден.
    /// </summary>
    [HttpDelete("courses/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCourse(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return NotFound();
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Возвращает список всех уроков указанного курса для управления содержимым.
    /// Уроки сортируются по порядковому номеру. Включает полные данные каждого урока.
    /// </summary>
    [HttpGet("courses/{id}/lessons")]
    [ProducesResponseType(typeof(AdminLessonResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminLessonResponse[]>> Lessons(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lessons = await db.Lessons
            .Where(l => l.CourseId == id)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);

        return Ok(lessons.Select(ToResponse).ToArray());
    }

    /// <summary>
    /// Создаёт новый урок в указанном курсе.
    /// Проверяет существование курса перед созданием.
    /// Возвращает 201 с данными созданного урока и ссылкой на получение его деталей.
    /// </summary>
    [HttpPost("lessons")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminLessonResponse>> CreateLesson(CreateLessonRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId, ct))
        {
            return BadRequest(new ErrorResponse("invalid_course", "Course not found"));
        }

        var lesson = new Lesson
        {
            CourseId = request.CourseId,
            Slug = request.Slug,
            Title = request.Title,
            Summary = request.Summary,
            ContentMarkdown = request.ContentMarkdown,
            SortOrder = request.SortOrder,
            EstimatedMinutes = request.EstimatedMinutes,
            IsPublished = request.IsPublished,
            GrammarTopicId = request.GrammarTopicId,
            KeyVocabulary = request.KeyVocabulary
        };

        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetLesson), new { id = lesson.Id }, ToResponse(lesson));
    }

    /// <summary>
    /// Получает детальную информацию об уроке по идентификатору для редактирования.
    /// Возвращает 404, если урок не найден.
    /// </summary>
    [HttpGet("lessons/{id}")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminLessonResponse>> GetLesson(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);
        return lesson is null ? NotFound() : Ok(ToResponse(lesson));
    }

    /// <summary>
    /// Обновляет параметры существующего урока.
    /// Поддерживает частичное обновление — изменяются только переданные поля.
    /// Возвращает 404, если урок не найден.
    /// </summary>
    [HttpPut("lessons/{id}")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminLessonResponse>> UpdateLesson(Guid id, UpdateLessonRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lesson is null)
        {
            return NotFound();
        }

        if (request.Slug is not null) lesson.Slug = request.Slug;
        if (request.Title is not null) lesson.Title = request.Title;
        if (request.Summary is not null) lesson.Summary = request.Summary;
        if (request.ContentMarkdown is not null) lesson.ContentMarkdown = request.ContentMarkdown;
        if (request.SortOrder is { } order) lesson.SortOrder = order;
        if (request.EstimatedMinutes is { } minutes) lesson.EstimatedMinutes = minutes;
        if (request.IsPublished is { } published) lesson.IsPublished = published;
        if (request.GrammarTopicId is { } topicId) lesson.GrammarTopicId = topicId;
        if (request.KeyVocabulary is { } vocab) lesson.KeyVocabulary = vocab;

        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(lesson));
    }

    /// <summary>
    /// Удаляет урок из курса.
    /// Операция необратима. Возвращает 404, если урок не найден.
    /// </summary>
    [HttpDelete("lessons/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteLesson(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lesson is null)
        {
            return NotFound();
        }

        db.Lessons.Remove(lesson);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Генерирует содержимое урока с помощью ИИ на основе указанной темы, уровня и требований.
    /// Использует текущие настройки промпта из конфигурации системы.
    /// Возвращает заголовок, краткое описание, Markdown-контент и ключевую лексику.
    /// </summary>
    [HttpPost("lessons/{id}/generate")]
    [ProducesResponseType(typeof(GenerateLessonContentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateLessonContentResponse>> GenerateLesson(
        Guid id, [FromBody] GenerateLessonContentRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new InvalidOperationException("Lesson not found");
        var lessonPrompt = await LoadLessonPromptAsync(ct);
        var result = await contentGeneration.GenerateLessonAsync(
            lesson.CourseId, request.Topic, request.Level, request.Requirements, request.Provider, request.Model, lessonPrompt, request.DurationMinutes, request.Summary, ct);

        return Ok(new GenerateLessonContentResponse(
            result.Title, result.Summary, result.ContentMarkdown, result.KeyVocabulary,
            result.Provider, result.Model, result.InputTokens, result.OutputTokens));
    }

    /// <summary>
    /// Генерирует структуру курса с помощью ИИ: описание и список заголовков уроков на основе темы и уровня.
    /// Позволяет быстро создать каркас курса, который затем можно доработать вручную.
    /// Возвращает 404, если курс не найден.
    /// </summary>
    [HttpPost("courses/{id}/generate")]
    [ProducesResponseType(typeof(GenerateCourseContentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateCourseContentResponse>> GenerateCourse(
        Guid id, [FromBody] GenerateCourseContentRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return NotFound();
        }

        var result = await contentGeneration.GenerateCourseAsync(
            course.LanguageId, request.Topic, request.Level, request.LessonsCount, request.Provider, request.Model, ct);

        return Ok(new GenerateCourseContentResponse(
            result.Description, result.LessonTitles, result.Provider, result.Model, result.InputTokens, result.OutputTokens));
    }

    /// <summary>
    /// Возвращает текущие настройки ИИ: провайдеры, модели, температуру, лимиты токенов и промпт для уроков.
    /// Объединяет значения из конфигурации приложения с переопределениями из базы данных.
    /// </summary>
    [HttpGet("ai-settings")]
    [ProducesResponseType(typeof(AiSettingsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiSettingsResponse>> GetAiSettings(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await BuildAiSettingsResponse(db, ct));
    }

    /// <summary>
    /// Обновляет настройки ИИ, сохраняя их в базе данных как JSON.
    /// Поддерживает частичное обновление — изменяются только переданные поля.
    /// Позволяет настроить провайдеров, модели, температуру, лимиты и промпт для генерации уроков.
    /// </summary>
    [HttpPut("ai-settings")]
    [ProducesResponseType(typeof(AiSettingsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiSettingsResponse>> UpdateAiSettings(UpdateAiSettingsRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AiSettingsKey, ct);
        var json = setting?.Value?.DeepClone() as JsonObject ?? new JsonObject();

        if (request.DefaultChatProvider is { } p) json["defaultChatProvider"] = p;
        if (request.DefaultEmbeddingProvider is { } e) json["defaultEmbeddingProvider"] = e;
        if (request.DefaultChatModel is { } m) json["defaultChatModel"] = m;
        if (request.Temperature is { } t) json["temperature"] = t;
        if (request.MaxOutputTokens is { } max) json["maxOutputTokens"] = max;
        if (request.LessonPrompt is { } lp) json["lessonPrompt"] = lp;

        if (request.Providers is { Count: > 0 })
        {
            var providers = new JsonObject();
            foreach (var provider in request.Providers)
            {
                providers[provider.Name] = new JsonObject
                {
                    ["kind"] = provider.Kind ?? "openai",
                    ["baseUrl"] = provider.BaseUrl ?? "",
                    ["apiKey"] = provider.ApiKey ?? "",
                    ["chatModel"] = provider.ChatModel ?? "",
                    ["embeddingModel"] = provider.EmbeddingModel ?? "",
                    ["ttsModel"] = provider.TtsModel ?? "",
                    ["sttModel"] = provider.SttModel ?? "",
                    ["enabled"] = provider.Enabled ?? true,
                    ["timeoutSeconds"] = provider.TimeoutSeconds ?? 120,
                    ["requestsPerMinute"] = provider.RequestsPerMinute ?? 120
                };
            }
            json["providers"] = providers;
        }

        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = AiSettingsKey, Value = json });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await BuildAiSettingsResponse(db, ct));
    }

    /// <summary>
    /// Проверяет подключение к указанному провайдеру ИИ, отправляя тестовый запрос.
    /// Измеряет задержку ответа и возвращает результат с указанием используемой модели.
    /// При ошибке возвращает 200 с флагом неуспеха и описанием проблемы.
    /// </summary>
    [HttpPost("ai-settings/test")]
    [ProducesResponseType(typeof(AiTestResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiTestResponse>> TestAiConnection(
        [FromQuery] string? provider, [FromQuery] string? model, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = gateway.ResolveChat(provider);
            var result = await client.CompleteAsync(new AiChatRequest
            {
                Messages = [AiChatMessage.User("ping")],
                MaxTokens = 8,
                Temperature = 0
            }, ct);

            return Ok(new AiTestResponse(true, client.Name, result.Model, sw.ElapsedMilliseconds, null));
        }
        catch (Exception ex)
        {
            return Ok(new AiTestResponse(false, provider ?? "default", null, sw.ElapsedMilliseconds, ex.Message));
        }
    }

    private async Task<string?> LoadLessonPromptAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AiSettingsKey, ct);
        var json = setting?.Value as JsonObject;
        return json?["lessonPrompt"]?.ToString();
    }

    private async Task<AiSettingsResponse> BuildAiSettingsResponse(AppDbContext db, CancellationToken ct)
    {
        var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AiSettingsKey, ct);
        var json = setting?.Value as JsonObject ?? new JsonObject();
        var opts = aiOptions.Value;

        var providersObj = json["providers"] as JsonObject ?? new JsonObject();
        var providers = opts.Providers.Select(pair =>
        {
            var saved = providersObj[pair.Key] as JsonObject;
            return new AiProviderSettingsResponse
            {
                Name = pair.Key,
                Kind = saved?["kind"]?.GetValue<string>() ?? pair.Value.Kind,
                BaseUrl = saved?["baseUrl"]?.GetValue<string>() ?? pair.Value.BaseUrl,
                ApiKey = saved?["apiKey"]?.GetValue<string>() ?? pair.Value.ApiKey,
                ChatModel = saved?["chatModel"]?.GetValue<string>() ?? pair.Value.ChatModel,
                EmbeddingModel = saved?["embeddingModel"]?.GetValue<string>() ?? pair.Value.EmbeddingModel,
                TtsModel = saved?["ttsModel"]?.GetValue<string>() ?? pair.Value.TtsModel,
                SttModel = saved?["sttModel"]?.GetValue<string>() ?? pair.Value.SttModel,
                Enabled = saved?["enabled"]?.GetValue<bool>() ?? pair.Value.Enabled,
                TimeoutSeconds = saved?["timeoutSeconds"]?.GetValue<int>() ?? pair.Value.TimeoutSeconds,
                RequestsPerMinute = saved?["requestsPerMinute"]?.GetValue<int>() ?? pair.Value.RequestsPerMinute
            };
        }).ToList();

        return new AiSettingsResponse
        {
            DefaultChatProvider = json["defaultChatProvider"]?.GetValue<string>() ?? opts.DefaultChatProvider,
            DefaultEmbeddingProvider = json["defaultEmbeddingProvider"]?.GetValue<string>() ?? opts.DefaultEmbeddingProvider,
            DefaultChatModel = json["defaultChatModel"]?.GetValue<string>() ?? opts.DefaultChatModel,
            Temperature = json["temperature"]?.GetValue<double>() ?? opts.Temperature,
            MaxOutputTokens = json["maxOutputTokens"]?.GetValue<int>() ?? opts.MaxOutputTokens,
            LessonPrompt = json["lessonPrompt"]?.ToString() ?? string.Empty,
            Providers = providers
        };
    }

    private static AdminCourseResponse ToResponse(Course c) => new(
        c.Id, c.Slug, c.Title, c.Description, c.Level.ToString(), c.LanguageId,
        c.Language?.Code ?? string.Empty, c.CoverUrl, c.AccentColor, c.EstimatedMinutes,
        c.IsPublished, c.SortOrder, c.Lessons.Count, c.CreatedAt);

    private static AdminLessonResponse ToResponse(Lesson l) => new(
        l.Id, l.CourseId, l.Slug, l.Title, l.Summary, l.ContentMarkdown, l.SortOrder,
        l.EstimatedMinutes, l.IsPublished, l.GrammarTopicId, l.KeyVocabulary);
}
