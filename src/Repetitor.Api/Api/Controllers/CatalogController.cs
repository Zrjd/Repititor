using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Api.Controllers;

[ApiController]
[Route("api/v1/catalog")]
[AllowAnonymous]
public sealed class CatalogController(IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var languages = await db.Languages
            .Where(l => l.IsEnabled)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.NameEnglish)
            .ToListAsync(ct);

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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var language = await db.Languages.FirstOrDefaultAsync(l => l.Code == code.ToLowerInvariant(), ct);
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var userId = CurrentUserAccessor.GetUserId(User);

        var query = db.Courses
            .Include(c => c.Language)
            .Include(c => c.Lessons)
            .Where(c => c.IsPublished);

        if (languageId is { } lid)
        {
            query = query.Where(c => c.LanguageId == lid);
        }

        if (level is { } lvl)
        {
            query = query.Where(c => c.Level == lvl);
        }

        var courses = await query
            .OrderBy(c => c.Language!.SortOrder)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Title)
            .ToListAsync(ct);

        var enrollments = userId == Guid.Empty
            ? []
            : await db.Enrollments
                .Include(e => e.LessonProgress)
                .Where(e => e.UserId == userId)
                .ToDictionaryAsync(e => e.CourseId, ct);

        var result = courses.Select(c =>
        {
            var enrollment = enrollments.GetValueOrDefault(c.Id);
            var progress = enrollment?.LessonProgress.Count ?? 0;
            var percent = c.Lessons.Count == 0 ? 0 : (int)Math.Round(progress * 100d / c.Lessons.Count);
            return new CourseResponse(
                c.Id, c.Slug, c.Title, c.Description, c.Level.ToString(),
                c.LanguageId, c.Language!.Code, c.CoverUrl, c.AccentColor, c.EstimatedMinutes,
                c.Lessons.Count(l => l.IsPublished), percent, enrollment is not null);
        }).ToArray();

        return Ok(result);
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses
            .Include(c => c.Language)
            .Include(c => c.Lessons)
            .FirstOrDefaultAsync(c => c.Slug == slug, ct);

        if (course is null)
        {
            return NotFound();
        }

        var userId = CurrentUserAccessor.GetUserId(User);
        var enrollment = userId == Guid.Empty
            ? null
            : await db.Enrollments.Include(e => e.LessonProgress).FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == course.Id, ct);
        var progress = enrollment?.LessonProgress.Count ?? 0;

        return Ok(new CourseResponse(
            course.Id, course.Slug, course.Title, course.Description, course.Level.ToString(),
            course.LanguageId, course.Language!.Code, course.CoverUrl, course.AccentColor, course.EstimatedMinutes,
            course.Lessons.Count(l => l.IsPublished),
            course.Lessons.Count == 0 ? 0 : (int)Math.Round(progress * 100d / course.Lessons.Count),
            enrollment is not null));
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var courseId = await db.Courses.Where(c => c.Slug == slug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (courseId is null)
        {
            return NotFound();
        }

        var userId = CurrentUserAccessor.GetUserId(User);
        var progress = userId == Guid.Empty
            ? new Dictionary<Guid, LessonProgressRow>()
            : await db.Enrollments
                .Where(e => e.UserId == userId && e.CourseId == courseId)
                .SelectMany(e => e.LessonProgress)
                .Select(p => new LessonProgressRow(p.LessonId, p.Status.ToString(), p.ProgressPercent, p.BestScorePercent))
                .ToDictionaryAsync(p => p.LessonId, ct);

        var lessons = await db.Lessons
            .Where(l => l.CourseId == courseId && l.IsPublished)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);

        return Ok(lessons.Select(l =>
        {
            var p = progress.GetValueOrDefault(l.Id);
            return new LessonResponse(
                l.Id, l.CourseId, l.Slug, l.Title, l.Summary, l.SortOrder, l.EstimatedMinutes,
                l.KeyVocabulary, null,
                p?.Status ?? nameof(Domain.Enums.LessonProgressStatus.NotStarted),
                p?.ProgressPercent ?? 0,
                p?.BestScorePercent ?? 0);
        }).ToArray());
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson is null)
        {
            return NotFound();
        }

        var userId = CurrentUserAccessor.GetUserId(User);
        var progress = userId == Guid.Empty
            ? null
            : await db.Enrollments
                .Where(e => e.UserId == userId && e.CourseId == lesson.CourseId)
                .SelectMany(e => e.LessonProgress)
                .FirstOrDefaultAsync(p => p.LessonId == lessonId, ct);

        return Ok(new LessonResponse(
            lesson.Id, lesson.CourseId, lesson.Slug, lesson.Title, lesson.Summary,
            lesson.SortOrder, lesson.EstimatedMinutes, lesson.KeyVocabulary, lesson.ContentMarkdown,
            progress?.Status.ToString() ?? nameof(Domain.Enums.LessonProgressStatus.NotStarted),
            progress?.ProgressPercent ?? 0,
            progress?.BestScorePercent ?? 0));
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.GrammarTopics.Where(g => g.IsPublished);

        if (languageId is { } lid)
        {
            query = query.Where(g => g.LanguageId == lid);
        }

        if (maxLevel is { } lvl)
        {
            query = query.Where(g => g.MinLevel <= lvl);
        }

        var topics = await query
            .OrderBy(g => g.MinLevel)
            .ThenBy(g => g.Title)
            .Select(g => new GrammarTopicResponse(g.Id, g.Slug, g.Title, g.Summary, g.MinLevel.ToString(), g.ExplanationMarkdown))
            .ToListAsync(ct);

        return Ok(topics);
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = await db.GrammarTopics.FirstOrDefaultAsync(g => g.Id == id, ct);
        return topic is null
            ? NotFound()
            : Ok(new GrammarTopicResponse(topic.Id, topic.Slug, topic.Title, topic.Summary,
                topic.MinLevel.ToString(), topic.ExplanationMarkdown));
    }

    private sealed record LessonProgressRow(Guid LessonId, string Status, int ProgressPercent, int BestScorePercent);
}
