using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="ICatalogDbService"/> поверх фабрики контекстов EF Core.
/// Прогресс пользователя всегда считается по его записям на курсы, поэтому каждый
/// публичный метод дополнительно фильтрует данные по идентификатору пользователя.
/// </summary>
public sealed class CatalogDbService(IDbContextFactory<AppDbContext> dbFactory) : ICatalogDbService
{
    public async Task<IReadOnlyList<Language>> GetEnabledLanguagesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages
            .Where(l => l.IsEnabled)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.NameEnglish)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Language>> GetAllLanguagesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages.OrderBy(l => l.SortOrder).ToListAsync(ct);
    }

    public async Task<Language?> FindLanguageByCodeAsync(string code, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages.FirstOrDefaultAsync(l => l.Code == code, ct);
    }

    public async Task<Language?> FindLanguageAsync(Guid languageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages.AsNoTracking().FirstOrDefaultAsync(l => l.Id == languageId, ct);
    }

    public async Task<string?> ResolveLanguageCodeAsync(Guid? languageId, CancellationToken ct)
    {
        if (languageId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.Code)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> LanguageExistsAsync(Guid languageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Languages.AnyAsync(l => l.Id == languageId, ct);
    }

    public async Task<IReadOnlyList<CourseCatalogItem>> GetPublishedCoursesAsync(
        Guid userId,
        Guid? languageId,
        CefrLevel? level,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

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

        // Прогресс нужен сразу для всех курсов, поэтому записи на курсы загружаются одним запросом.
        var enrollments = userId == Guid.Empty
            ? []
            : await db.Enrollments
                .Include(e => e.LessonProgress)
                .Where(e => e.UserId == userId)
                .ToDictionaryAsync(e => e.CourseId, ct);

        return courses.Select(c => MapCourse(c, enrollments.GetValueOrDefault(c.Id))).ToArray();
    }

    public async Task<CourseCatalogItem?> GetPublishedCourseAsync(Guid userId, string slug, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses
            .Include(c => c.Language)
            .Include(c => c.Lessons)
            .FirstOrDefaultAsync(c => c.Slug == slug, ct);

        if (course is null)
        {
            return null;
        }

        var enrollment = userId == Guid.Empty
            ? null
            : await db.Enrollments
                .Include(e => e.LessonProgress)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == course.Id, ct);

        return MapCourse(course, enrollment);
    }

    public async Task<Guid?> GetCourseIdBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Courses.Where(c => c.Slug == slug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<CourseLessonsResult> GetCourseLessonsAsync(Guid userId, string slug, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var courseId = await db.Courses.Where(c => c.Slug == slug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (courseId is null)
        {
            return new CourseLessonsResult(false, []);
        }

        var progress = userId == Guid.Empty
            ? new Dictionary<Guid, ProgressRow>()
            : (await db.Enrollments
                .Where(e => e.UserId == userId && e.CourseId == courseId)
                .SelectMany(e => e.LessonProgress)
                .Select(p => new { p.LessonId, Status = p.Status.ToString(), p.ProgressPercent, p.BestScorePercent })
                .ToListAsync(ct))
                .ToDictionary(p => p.LessonId, p => new ProgressRow(p.Status, p.ProgressPercent, p.BestScorePercent));

        var lessons = await db.Lessons
            .Where(l => l.CourseId == courseId && l.IsPublished)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);

        return new CourseLessonsResult(true, lessons.Select(l =>
        {
            var row = progress.GetValueOrDefault(l.Id);
            return new LessonCatalogItem(
                l.Id, l.CourseId, l.Slug, l.Title, l.Summary, l.SortOrder, l.EstimatedMinutes,
                l.KeyVocabulary, null,
                row?.Status ?? nameof(LessonProgressStatus.NotStarted),
                row?.ProgressPercent ?? 0,
                row?.BestScorePercent ?? 0);
        }).ToArray());
    }

    public async Task<LessonCatalogItem?> GetLessonAsync(Guid userId, Guid lessonId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson is null)
        {
            return null;
        }

        var progress = userId == Guid.Empty
            ? null
            : await db.Enrollments
                .Where(e => e.UserId == userId && e.CourseId == lesson.CourseId)
                .SelectMany(e => e.LessonProgress)
                .FirstOrDefaultAsync(p => p.LessonId == lessonId, ct);

        return new LessonCatalogItem(
            lesson.Id, lesson.CourseId, lesson.Slug, lesson.Title, lesson.Summary,
            lesson.SortOrder, lesson.EstimatedMinutes, lesson.KeyVocabulary, lesson.ContentMarkdown,
            progress?.Status.ToString() ?? nameof(LessonProgressStatus.NotStarted),
            progress?.ProgressPercent ?? 0,
            progress?.BestScorePercent ?? 0);
    }

    public async Task<IReadOnlyList<GrammarTopicItem>> GetGrammarTopicsAsync(
        Guid? languageId,
        CefrLevel? maxLevel,
        CancellationToken ct)
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

        return await query
            .OrderBy(g => g.MinLevel)
            .ThenBy(g => g.Title)
            .Select(g => new GrammarTopicItem(
                g.Id, g.Slug, g.Title, g.Summary, g.MinLevel.ToString(), g.ExplanationMarkdown))
            .ToListAsync(ct);
    }

    public async Task<GrammarTopicItem?> GetGrammarTopicAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = await db.GrammarTopics.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return topic is null
            ? null
            : new GrammarTopicItem(topic.Id, topic.Slug, topic.Title, topic.Summary, topic.MinLevel.ToString(), topic.ExplanationMarkdown);
    }

    public async Task<IReadOnlyList<AdminCourseItem>> GetAdminCoursesAsync(
        Guid? languageId,
        bool includeUnpublished,
        CancellationToken ct)
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

        return courses.Select(MapAdminCourse).ToArray();
    }

    public async Task<AdminCourseItem?> GetAdminCourseAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.Include(c => c.Language).FirstOrDefaultAsync(c => c.Id == id, ct);
        return course is null ? null : MapAdminCourse(course);
    }

    public async Task<AdminCourseItem> CreateCourseAsync(Course course, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return MapAdminCourse(course);
    }

    public async Task<CourseUpdateResult> UpdateCourseAsync(Guid id, CourseUpdate update, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Смену языка проверяем до правок, чтобы курс не остался со ссылкой на несуществующий язык.
        if (update.LanguageId is { } languageId && !await db.Languages.AnyAsync(l => l.Id == languageId, ct))
        {
            return new CourseUpdateResult(null, false, true);
        }

        var course = await db.Courses.Include(c => c.Language).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return new CourseUpdateResult(null, true, false);
        }

        if (update.Slug is not null) course.Slug = update.Slug;
        if (update.Title is not null) course.Title = update.Title;
        if (update.Description is not null) course.Description = update.Description;
        if (update.Level is { } level) course.Level = level;
        if (update.LanguageId is { } langId) course.LanguageId = langId;
        if (update.CoverUrl is not null) course.CoverUrl = update.CoverUrl;
        if (update.AccentColor is not null) course.AccentColor = update.AccentColor;
        if (update.EstimatedMinutes is { } minutes) course.EstimatedMinutes = minutes;
        if (update.IsPublished is { } published) course.IsPublished = published;
        if (update.SortOrder is { } order) course.SortOrder = order;

        await db.SaveChangesAsync(ct);
        return new CourseUpdateResult(MapAdminCourse(course), false, false);
    }

    public async Task<bool> DeleteCourseAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return false;
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CourseExistsAsync(Guid courseId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Courses.AnyAsync(c => c.Id == courseId, ct);
    }

    public async Task<IReadOnlyList<AdminLessonItem>> GetAdminCourseLessonsAsync(Guid courseId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lessons = await db.Lessons
            .Where(l => l.CourseId == courseId)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);
        return lessons.Select(MapAdminLesson).ToArray();
    }

    public async Task<AdminLessonItem?> GetAdminLessonAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);
        return lesson is null ? null : MapAdminLesson(lesson);
    }

    public async Task<AdminLessonItem> CreateLessonAsync(Lesson lesson, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(ct);
        return MapAdminLesson(lesson);
    }

    public async Task<AdminLessonItem?> UpdateLessonAsync(Guid id, LessonUpdate update, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lesson is null)
        {
            return null;
        }

        if (update.Slug is not null) lesson.Slug = update.Slug;
        if (update.Title is not null) lesson.Title = update.Title;
        if (update.Summary is not null) lesson.Summary = update.Summary;
        if (update.ContentMarkdown is not null) lesson.ContentMarkdown = update.ContentMarkdown;
        if (update.SortOrder is { } order) lesson.SortOrder = order;
        if (update.EstimatedMinutes is { } minutes) lesson.EstimatedMinutes = minutes;
        if (update.IsPublished is { } published) lesson.IsPublished = published;
        if (update.GrammarTopicId is { } topicId) lesson.GrammarTopicId = topicId;
        if (update.KeyVocabulary is { } vocab) lesson.KeyVocabulary = vocab;

        await db.SaveChangesAsync(ct);
        return MapAdminLesson(lesson);
    }

    public async Task<bool> DeleteLessonAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lesson is null)
        {
            return false;
        }

        db.Lessons.Remove(lesson);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid?> GetLessonCourseIdAsync(Guid lessonId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Lessons.AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => (Guid?)l.CourseId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> GetCourseLanguageIdAsync(Guid courseId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Courses.AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => (Guid?)c.LanguageId)
            .FirstOrDefaultAsync(ct);
    }

    private static CourseCatalogItem MapCourse(Course course, CourseEnrollment? enrollment)
    {
        var progress = enrollment?.LessonProgress.Count ?? 0;
        var percent = course.Lessons.Count == 0
            ? 0
            : (int)Math.Round(progress * 100d / course.Lessons.Count);

        return new CourseCatalogItem(
            course.Id, course.Slug, course.Title, course.Description, course.Level.ToString(),
            course.LanguageId, course.Language?.Code ?? string.Empty, course.CoverUrl, course.AccentColor,
            course.EstimatedMinutes, course.Lessons.Count(l => l.IsPublished), percent, enrollment is not null);
    }

    private static AdminCourseItem MapAdminCourse(Course course) => new(
        course.Id, course.Slug, course.Title, course.Description, course.Level.ToString(), course.LanguageId,
        course.Language?.Code ?? string.Empty, course.CoverUrl, course.AccentColor, course.EstimatedMinutes,
        course.IsPublished, course.SortOrder, course.Lessons.Count, course.CreatedAt);

    private static AdminLessonItem MapAdminLesson(Lesson lesson) => new(
        lesson.Id, lesson.CourseId, lesson.Slug, lesson.Title, lesson.Summary, lesson.ContentMarkdown,
        lesson.SortOrder, lesson.EstimatedMinutes, lesson.IsPublished, lesson.GrammarTopicId, lesson.KeyVocabulary);

    /// <summary>Строка прогресса по уроку, вырожденная до трёх полей, нужных в каталоге.</summary>
    private sealed record ProgressRow(string Status, int ProgressPercent, int BestScorePercent);
}
