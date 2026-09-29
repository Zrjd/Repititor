using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api.Controllers;

/// <summary>
/// Кабинет учителя: собственные курсы и уроки.
/// Все операции работают только с материалами текущего учителя — чужие курсы и уроки
/// возвращают 403, администратору доступны все курсы.
/// </summary>
[ApiController]
[Route("api/v1/teacher")]
[Authorize(Roles = "Teacher,Admin")]
public sealed class TeacherController(
    ICatalogDbService catalog,
    IContentGenerationService contentGeneration,
    ILessonGenerationService lessonGeneration,
    IAiDbService aiDb) : ControllerBase
{
    private const string AiSettingsKey = "ai.settings";

    /// <summary>
    /// Возвращает все курсы учителя, включая черновики.
    /// Поддерживает фильтрацию по языку и по статусу публикации.
    /// </summary>
    [HttpGet("courses")]
    [ProducesResponseType(typeof(AdminCourseResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCourseResponse[]>> Courses(
        [FromQuery] Guid? languageId, [FromQuery] bool includeUnpublished = true, CancellationToken ct = default)
    {
        var courses = await catalog.GetAdminCoursesAsync(Actor(), languageId, includeUnpublished, ct);
        return Ok(courses.Select(ToResponse).ToArray());
    }

    /// <summary>
    /// Создаёт новый курс учителя.
    /// Курс создаётся в статусе черновика: опубликовать его можно отдельным запросом.
    /// Возвращает 201 с данными созданного курса.
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
            SortOrder = request.SortOrder
        };

        var created = await catalog.CreateCourseAsync(Actor(), course, ct);
        return CreatedAtAction(nameof(GetCourse), new { id = created.Id }, ToResponse(created));
    }

    /// <summary>
    /// Возвращает курс учителя по идентификатору.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpGet("courses/{id}")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminCourseResponse>> GetCourse(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAdminCourseAsync(Actor(), id, ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => Ok(ToResponse(result.Value!))
        };
    }

    /// <summary>
    /// Частично обновляет курс учителя. Статус публикации здесь не меняется —
    /// для этого есть отдельные действия публикации и снятия с публикации.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpPut("courses/{id}")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminCourseResponse>> UpdateCourse(Guid id, UpdateCourseRequest request, CancellationToken ct)
    {
        var result = await catalog.UpdateCourseAsync(
            Actor(),
            id,
            new CourseUpdate(
                request.Slug, request.Title, request.Description, request.Level, request.LanguageId,
                request.CoverUrl, request.AccentColor, request.EstimatedMinutes, IsPublished: null, request.SortOrder),
            ct);

        return result.Status switch
        {
            CatalogMutationStatus.InvalidLanguage => BadRequest(new ErrorResponse("invalid_language", "Language not found")),
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => Ok(ToResponse(result.Value!))
        };
    }

    /// <summary>
    /// Публикует курс учителя, делая его доступным всем пользователям каталога.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpPost("courses/{id}/publish")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminCourseResponse>> PublishCourse(Guid id, CancellationToken ct)
    {
        return await SetPublishedAsync(id, true, ct);
    }

    /// <summary>
    /// Снимает курс учителя с публикации, возвращая его в статус черновика.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpPost("courses/{id}/unpublish")]
    [ProducesResponseType(typeof(AdminCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminCourseResponse>> UnpublishCourse(Guid id, CancellationToken ct)
    {
        return await SetPublishedAsync(id, false, ct);
    }

    /// <summary>
    /// Удаляет курс учителя вместе с его уроками.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpDelete("courses/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteCourse(Guid id, CancellationToken ct)
    {
        var result = await catalog.DeleteCourseAsync(Actor(), id, ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => NoContent()
        };
    }

    /// <summary>
    /// Возвращает все уроки курса учителя, включая неопубликованные.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpGet("courses/{id}/lessons")]
    [ProducesResponseType(typeof(AdminLessonResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLessonResponse[]>> Lessons(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAdminCourseLessonsAsync(Actor(), id, ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => Ok(result.Value!.Select(ToResponse).ToArray())
        };
    }

    /// <summary>
    /// Создаёт урок в курсе учителя.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpPost("courses/{courseId}/lessons")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLessonResponse>> CreateLesson(Guid courseId, CreateLessonRequest request, CancellationToken ct)
    {
        request.CourseId = courseId;
        var result = await catalog.CreateLessonAsync(Actor(), ToLesson(request, courseId), ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => CreatedAtAction(nameof(GetLesson), new { id = result.Value!.Id }, ToResponse(result.Value))
        };
    }

    /// <summary>
    /// Возвращает урок учителя по идентификатору.
    /// Возвращает 404, если урок не найден, и 403, если он принадлежит курсу другого учителя.
    /// </summary>
    [HttpGet("lessons/{id}")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLessonResponse>> GetLesson(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAdminLessonAsync(Actor(), id, ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => Ok(ToResponse(result.Value!))
        };
    }

    /// <summary>
    /// Частично обновляет урок учителя.
    /// Возвращает 404, если урок не найден, и 403, если он принадлежит курсу другого учителя.
    /// </summary>
    [HttpPut("lessons/{id}")]
    [ProducesResponseType(typeof(AdminLessonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLessonResponse>> UpdateLesson(Guid id, UpdateLessonRequest request, CancellationToken ct)
    {
        var result = await catalog.UpdateLessonAsync(
            Actor(),
            id,
            new LessonUpdate(
                request.Slug, request.Title, request.Summary, request.ContentMarkdown,
                request.SortOrder, request.EstimatedMinutes, request.IsPublished,
                request.GrammarTopicId, request.KeyVocabulary),
            ct);

        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => Ok(ToResponse(result.Value!))
        };
    }

    /// <summary>
    /// Удаляет урок из курса учителя.
    /// Возвращает 404, если урок не найден, и 403, если он принадлежит курсу другого учителя.
    /// </summary>
    [HttpDelete("lessons/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteLesson(Guid id, CancellationToken ct)
    {
        var result = await catalog.DeleteLessonAsync(Actor(), id, ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => NoContent()
        };
    }

    /// <summary>
    /// Генерирует содержимое урока с помощью ИИ по теме, уровню и требованиям учителя.
    /// Возвращает 404, если урок не найден, и 403, если он принадлежит курсу другого учителя.
    /// </summary>
    [HttpPost("lessons/{id}/generate")]
    [ProducesResponseType(typeof(LessonGenerationStateResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(GenerateLessonContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GenerateLessonContentResponse>> GenerateLesson(
        Guid id, [FromBody] GenerateLessonContentRequest request, bool wait = false, CancellationToken ct = default)
    {
        var course = await catalog.GetLessonCourseIdAsync(Actor(), id, ct);
        if (course.Status == CatalogMutationStatus.Forbidden)
        {
            return Forbid();
        }

        if (course.Status == CatalogMutationStatus.NotFound)
        {
            return NotFound();
        }

        var lessonPrompt = (await aiDb.GetSettingAsync(AiSettingsKey, ct))?["lessonPrompt"]?.ToString();

        // wait=true сохраняет прежнее поведение: ответ приходит с готовым содержимым.
        if (wait)
        {
            var result = await contentGeneration.GenerateLessonAsync(
                course.Value, request.Topic, request.Level, request.Requirements, request.Provider, request.Model,
                lessonPrompt, request.DurationMinutes, request.Summary, ct);

            return Ok(new GenerateLessonContentResponse(
                result.Title, result.Summary, result.ContentMarkdown, result.KeyVocabulary,
                result.Provider, result.Model, result.InputTokens, result.OutputTokens));
        }

        var queued = await lessonGeneration.EnqueueAsync(id, new LessonGenerationRequest(
            request.Topic, request.Level, request.Requirements, request.DurationMinutes,
            request.Summary, request.Provider, request.Model, lessonPrompt), ct);

        var state = await lessonGeneration.GetStateAsync(id, ct) ?? new LessonGenerationState(
            id, LessonGenerationStatus.Queued, null, null, null);
        var response = state.ToResponse();
        return queued == LessonGenerationEnqueue.AlreadyActive
            ? Conflict(response)
            : Accepted(response);
    }

    /// <summary>
    /// Возвращает состояние фоновой генерации урока.
    /// </summary>
    [HttpGet("lessons/{id}/generation")]
    [ProducesResponseType(typeof(LessonGenerationStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LessonGenerationStateResponse>> LessonGeneration(Guid id, CancellationToken ct)
    {
        var course = await catalog.GetLessonCourseIdAsync(Actor(), id, ct);
        if (course.Status == CatalogMutationStatus.Forbidden)
        {
            return Forbid();
        }

        if (course.Status == CatalogMutationStatus.NotFound)
        {
            return NotFound();
        }

        var state = await lessonGeneration.GetStateAsync(id, ct);
        return state is null ? NotFound() : Ok(state.ToResponse());
    }

    /// <summary>
    /// Генерирует структуру курса с помощью ИИ: описание и список заголовков уроков.
    /// Возвращает 404, если курс не найден, и 403, если он принадлежит другому учителю.
    /// </summary>
    [HttpPost("courses/{id}/generate")]
    [ProducesResponseType(typeof(GenerateCourseContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GenerateCourseContentResponse>> GenerateCourse(
        Guid id, [FromBody] GenerateCourseContentRequest request, CancellationToken ct)
    {
        var language = await catalog.GetCourseLanguageIdAsync(Actor(), id, ct);
        if (language.Status == CatalogMutationStatus.Forbidden)
        {
            return Forbid();
        }

        if (language.Status == CatalogMutationStatus.NotFound)
        {
            return NotFound();
        }

        var result = await contentGeneration.GenerateCourseAsync(
            language.Value, request.Topic, request.Level, request.LessonsCount, request.Provider, request.Model, ct);

        return Ok(new GenerateCourseContentResponse(
            result.Description, result.LessonTitles, result.Provider, result.Model, result.InputTokens, result.OutputTokens));
    }

    private async Task<ActionResult<AdminCourseResponse>> SetPublishedAsync(Guid id, bool isPublished, CancellationToken ct)
    {
        var result = await catalog.SetCoursePublishedAsync(Actor(), id, isPublished, ct);
        return result.Status switch
        {
            CatalogMutationStatus.NotFound => NotFound(),
            CatalogMutationStatus.Forbidden => Forbid(),
            _ => Ok(ToResponse(result.Value!))
        };
    }

    private static Lesson ToLesson(CreateLessonRequest request, Guid courseId) => new()
    {
        CourseId = courseId,
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

    /// <summary>
    /// Текущий пользователь как актор доступа к каталогу: слой данных ограничивает
    /// учителя его собственными курсами и уроками.
    /// </summary>
    private DbActor Actor() => new(
        CurrentUserAccessor.GetUserId(User),
        User.IsInRole(nameof(UserRole.Admin)) ? UserRole.Admin : UserRole.Teacher);

    private static AdminCourseResponse ToResponse(AdminCourseItem c) => new(
        c.Id, c.Slug, c.Title, c.Description, c.Level, c.LanguageId,
        c.LanguageCode, c.CoverUrl, c.AccentColor, c.EstimatedMinutes,
        c.IsPublished, c.SortOrder, c.LessonsCount, c.CreatedAt, c.OwnerDisplayName);

    private static AdminLessonResponse ToResponse(AdminLessonItem l) => new(
        l.Id, l.CourseId, l.Slug, l.Title, l.Summary, l.ContentMarkdown, l.SortOrder,
        l.EstimatedMinutes, l.IsPublished, l.GrammarTopicId, l.KeyVocabulary,
        l.AiGenerationStatus.ToString(), l.AiGenerationRequestedAt, l.AiGenerationCompletedAt,
        l.AiGenerationError, l.IsAvailableToStudents);
}
