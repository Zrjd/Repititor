using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/catalog")]
[AllowAnonymous]
public sealed class CatalogController(ICatalogDbService catalog) : ControllerBase
{
    /// <summary>
    /// Получает список всех доступных языков для обучения.
    /// Возвращает только включённые языки, отсортированные по порядку отображения и английскому названию.
    /// Используется для заполнения фильтров и выпадающих списков на клиенте.
    /// </summary>
    [HttpGet("languages")]
    [ProducesResponseType(typeof(LanguageResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<LanguageResponse[]>> Languages(CancellationToken ct)
    {
        var languages = await catalog.GetEnabledLanguagesAsync(ct);
        return Ok(languages.Select(l => l.ToResponse()).ToArray());
    }

    /// <summary>
    /// Получает информацию о конкретном языке по его коду (например, "en", "de").
    /// Код приводится к нижнему регистру для регистронезависимого поиска.
    /// Возвращает 404, если язык с указанным кодом не найден.
    /// </summary>
    [HttpGet("languages/{code}")]
    [ProducesResponseType(typeof(LanguageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LanguageResponse>> Language(string code, CancellationToken ct)
    {
        var language = await catalog.FindLanguageByCodeAsync(code.ToLowerInvariant(), ct);
        return language is null ? NotFound() : Ok(language.ToResponse());
    }

    /// <summary>
    /// Получает каталог опубликованных курсов с возможностью фильтрации по языку и уровню CEFR.
    /// Для каждого курса вычисляется прогресс текущего пользователя (процент пройденных уроков),
    /// что позволяет клиенту отображать персонализированные данные без дополнительных запросов.
    /// </summary>
    [HttpGet("courses")]
    [ProducesResponseType(typeof(CourseResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<CourseResponse[]>> Courses([FromQuery] Guid? languageId, [FromQuery] CefrLevel? level, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var courses = await catalog.GetPublishedCoursesAsync(userId, languageId, level, ct);

        return Ok(courses.Select(c => new CourseResponse(
            c.Id, c.Slug, c.Title, c.Description, c.Level,
            c.LanguageId, c.LanguageCode, c.CoverUrl, c.AccentColor, c.EstimatedMinutes,
            c.PublishedLessonsCount, c.ProgressPercent, c.IsEnrolled)).ToArray());
    }

    /// <summary>
    /// Получает детальную информацию о курсе по его читаемому идентификатору (slug).
    /// В отличие от списка курсов, включает полные данные о прогрессе пользователя.
    /// Возвращает 404, если курс с указанным slug не найден.
    /// </summary>
    [HttpGet("courses/{slug}")]
    [ProducesResponseType(typeof(CourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseResponse>> Course(string slug, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var course = await catalog.GetPublishedCourseAsync(userId, slug, ct);
        if (course is null)
        {
            return NotFound();
        }

        return Ok(new CourseResponse(
            course.Id, course.Slug, course.Title, course.Description, course.Level,
            course.LanguageId, course.LanguageCode, course.CoverUrl, course.AccentColor, course.EstimatedMinutes,
            course.PublishedLessonsCount, course.ProgressPercent, course.IsEnrolled));
    }

    /// <summary>
    /// Получает список всех опубликованных уроков для указанного курса.
    /// Для каждого урока возвращается текущий статус прогресса пользователя (начат/завершён, процент выполнения, лучший результат).
    /// Уроки сортируются по порядковому номеру в курсе.
    /// </summary>
    [HttpGet("courses/{slug}/lessons")]
    [ProducesResponseType(typeof(LessonResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<LessonResponse[]>> Lessons(string slug, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var result = await catalog.GetCourseLessonsAsync(userId, slug, ct);
        if (!result.CourseExists)
        {
            return NotFound();
        }

        return Ok(result.Lessons.Select(l => new LessonResponse(
            l.Id, l.CourseId, l.Slug, l.Title, l.Summary, l.SortOrder, l.EstimatedMinutes,
            l.KeyVocabulary, l.ContentMarkdown, l.Status, l.ProgressPercent, l.BestScorePercent)).ToArray());
    }

    /// <summary>
    /// Получает полную информацию об уроке по его уникальному идентификатору.
    /// Включает содержимое урока в формате Markdown и прогресс пользователя по данному уроку.
    /// Возвращает 404, если урок не найден.
    /// </summary>
    [HttpGet("lessons/{lessonId}")]
    [ProducesResponseType(typeof(LessonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LessonResponse>> Lesson(Guid lessonId, CancellationToken ct)
    {
        var userId = CurrentUserAccessor.GetUserId(User);
        var lesson = await catalog.GetLessonAsync(userId, lessonId, ct);
        if (lesson is null)
        {
            return NotFound();
        }

        return Ok(new LessonResponse(
            lesson.Id, lesson.CourseId, lesson.Slug, lesson.Title, lesson.Summary,
            lesson.SortOrder, lesson.EstimatedMinutes, lesson.KeyVocabulary, lesson.ContentMarkdown,
            lesson.Status, lesson.ProgressPercent, lesson.BestScorePercent));
    }

    /// <summary>
    /// Получает список опубликованных грамматических тем с фильтрацией по языку и максимальному уровню CEFR.
    /// Фильтр по уровню отсеивает темы, минимальный уровень которых выше указанного.
    /// Темы сортируются по минимальному уровню и названию для удобной навигации.
    /// </summary>
    [HttpGet("grammar")]
    [ProducesResponseType(typeof(GrammarTopicResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<GrammarTopicResponse[]>> Grammar([FromQuery] Guid? languageId, [FromQuery] CefrLevel? maxLevel, CancellationToken ct)
    {
        var topics = await catalog.GetGrammarTopicsAsync(languageId, maxLevel, ct);

        return Ok(topics.Select(g => new GrammarTopicResponse(
            g.Id, g.Slug, g.Title, g.Summary, g.MinLevel, g.ExplanationMarkdown)).ToArray());
    }

    /// <summary>
    /// Получает детальную информацию о грамматической теме по уникальному идентификатору.
    /// Включает полное объяснение грамматики в формате Markdown для отображения в учебнике.
    /// Возвращает 404, если тема не найдена.
    /// </summary>
    [HttpGet("grammar/{id}")]
    [ProducesResponseType(typeof(GrammarTopicResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GrammarTopicResponse>> GrammarTopic(Guid id, CancellationToken ct)
    {
        var topic = await catalog.GetGrammarTopicAsync(id, ct);
        return topic is null
            ? NotFound()
            : Ok(new GrammarTopicResponse(topic.Id, topic.Slug, topic.Title, topic.Summary, topic.MinLevel, topic.ExplanationMarkdown));
    }
}
