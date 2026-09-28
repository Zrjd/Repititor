using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Teacher")]
public sealed class AdminCatalogController(
    ICatalogDbService catalog,
    IAiDbService aiDb,
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
        var courses = await catalog.GetAdminCoursesAsync(languageId, includeUnpublished, ct);
        return Ok(courses.Select(ToResponse).ToArray());
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
        if (!await catalog.LanguageExistsAsync(request.LanguageId, ct))
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

        var created = await catalog.CreateCourseAsync(course, ct);
        return CreatedAtAction(nameof(GetCourse), new { id = created.Id }, ToResponse(created));
    }

    /// <summary>
    /// Получает детальную информацию о курсе по идентификатору для редактирования в панели управления.
    /// Возвращает 404, если курс не найден.
    /// </summary>
    [HttpGet("courses/{id}")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCourseResponse>> GetCourse(Guid id, CancellationToken ct)
    {
        var course = await catalog.GetAdminCourseAsync(id, ct);
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
        var result = await catalog.UpdateCourseAsync(
            id,
            new CourseUpdate(
                request.Slug, request.Title, request.Description, request.Level, request.LanguageId,
                request.CoverUrl, request.AccentColor, request.EstimatedMinutes, request.IsPublished, request.SortOrder),
            ct);

        if (result.InvalidLanguage)
        {
            return BadRequest(new ErrorResponse("invalid_language", "Language not found"));
        }

        return result.NotFound
            ? NotFound()
            : Ok(ToResponse(result.Course!));
    }

    /// <summary>
    /// Удаляет курс из каталога вместе со связанными данными.
    /// Операция необратима. Возвращает 404, если курс не найден.
    /// </summary>
    [HttpDelete("courses/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCourse(Guid id, CancellationToken ct)
    {
        return await catalog.DeleteCourseAsync(id, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Возвращает список всех уроков указанного курса для управления содержимым.
    /// Уроки сортируются по порядковому номеру. Включает полные данные каждого урока.
    /// </summary>
    [HttpGet("courses/{id}/lessons")]
    [ProducesResponseType(typeof(AdminLessonResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminLessonResponse[]>> Lessons(Guid id, CancellationToken ct)
    {
        var lessons = await catalog.GetAdminCourseLessonsAsync(id, ct);
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
        if (!await catalog.CourseExistsAsync(request.CourseId, ct))
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

        var created = await catalog.CreateLessonAsync(lesson, ct);
        return CreatedAtAction(nameof(GetLesson), new { id = created.Id }, ToResponse(created));
    }

    /// <summary>
    /// Получает детальную информацию об уроке по идентификатору для редактирования.
    /// Возвращает 404, если урок не найден.
    /// </summary>
    [HttpGet("lessons/{id}")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminLessonResponse>> GetLesson(Guid id, CancellationToken ct)
    {
        var lesson = await catalog.GetAdminLessonAsync(id, ct);
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
        var lesson = await catalog.UpdateLessonAsync(
            id,
            new LessonUpdate(
                request.Slug, request.Title, request.Summary, request.ContentMarkdown,
                request.SortOrder, request.EstimatedMinutes, request.IsPublished,
                request.GrammarTopicId, request.KeyVocabulary),
            ct);

        return lesson is null ? NotFound() : Ok(ToResponse(lesson));
    }

    /// <summary>
    /// Удаляет урок из курса.
    /// Операция необратима. Возвращает 404, если урок не найден.
    /// </summary>
    [HttpDelete("lessons/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteLesson(Guid id, CancellationToken ct)
    {
        return await catalog.DeleteLessonAsync(id, ct) ? NoContent() : NotFound();
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
        var courseId = await catalog.GetLessonCourseIdAsync(id, ct)
            ?? throw new InvalidOperationException("Lesson not found");
        var lessonPrompt = await LoadLessonPromptAsync(ct);
        var result = await contentGeneration.GenerateLessonAsync(
            courseId, request.Topic, request.Level, request.Requirements, request.Provider, request.Model, lessonPrompt, request.DurationMinutes, request.Summary, ct);

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
        var languageId = await catalog.GetCourseLanguageIdAsync(id, ct);
        if (languageId is null)
        {
            return NotFound();
        }

        var result = await contentGeneration.GenerateCourseAsync(
            languageId.Value, request.Topic, request.Level, request.LessonsCount, request.Provider, request.Model, ct);

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
        return Ok(await BuildAiSettingsResponse(ct));
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
        var saved = await aiDb.GetSettingAsync(AiSettingsKey, ct);
        var json = saved?.DeepClone() as JsonObject ?? new JsonObject();

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

        await aiDb.SaveSettingAsync(AiSettingsKey, json, clock.UtcNow, ct);
        return Ok(await BuildAiSettingsResponse(ct));
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
        var json = await aiDb.GetSettingAsync(AiSettingsKey, ct);
        return json?["lessonPrompt"]?.ToString();
    }

    /// <summary>
    /// Собирает ответ с настройками ИИ: значения из базы перекрывают конфигурацию приложения,
    /// поэтому редактирование через панель управления не требует перезапуска сервиса.
    /// </summary>
    private async Task<AiSettingsResponse> BuildAiSettingsResponse(CancellationToken ct)
    {
        var json = await aiDb.GetSettingAsync(AiSettingsKey, ct) ?? new JsonObject();
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

    private static AdminCourseResponse ToResponse(AdminCourseItem c) => new(
        c.Id, c.Slug, c.Title, c.Description, c.Level, c.LanguageId,
        c.LanguageCode, c.CoverUrl, c.AccentColor, c.EstimatedMinutes,
        c.IsPublished, c.SortOrder, c.LessonsCount, c.CreatedAt);

    private static AdminLessonResponse ToResponse(AdminLessonItem l) => new(
        l.Id, l.CourseId, l.Slug, l.Title, l.Summary, l.ContentMarkdown, l.SortOrder,
        l.EstimatedMinutes, l.IsPublished, l.GrammarTopicId, l.KeyVocabulary);
}
