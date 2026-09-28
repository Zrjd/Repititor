using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Доступ к справочнику языков и учебному каталогу: курсы, уроки, темы грамматики,
/// записи пользователей на курсы и их прогресс. Здесь же собраны операции админ-панели над каталогом.
/// </summary>
public interface ICatalogDbService
{
    /// <summary>Возвращает только включённые языки в порядке отображения — для фильтров и выпадающих списков.</summary>
    Task<IReadOnlyList<Language>> GetEnabledLanguagesAsync(CancellationToken ct);

    /// <summary>Возвращает все языки без фильтрации — используется при регистрации для выбора языков по умолчанию.</summary>
    Task<IReadOnlyList<Language>> GetAllLanguagesAsync(CancellationToken ct);

    /// <summary>Находит язык по его коду, например «en». Код сравнивается в нижнем регистре.</summary>
    Task<Language?> FindLanguageByCodeAsync(string code, CancellationToken ct);

    /// <summary>Находит язык по идентификатору — нужен, например, для получения кода языка слова.</summary>
    Task<Language?> FindLanguageAsync(Guid languageId, CancellationToken ct);

    /// <summary>Определяет код языка по его идентификатору. Возвращает null для пустого или неизвестного идентификатора.</summary>
    Task<string?> ResolveLanguageCodeAsync(Guid? languageId, CancellationToken ct);

    /// <summary>Проверяет, что язык с указанным идентификатором есть в справочнике.</summary>
    Task<bool> LanguageExistsAsync(Guid languageId, CancellationToken ct);

    /// <summary>
    /// Возвращает опубликованные курсы с фильтрами по языку и уровню, дополненные прогрессом пользователя.
    /// Для анонимного посетителя userId равен <see cref="Guid.Empty"/> и прогресс не рассчитывается.
    /// </summary>
    Task<IReadOnlyList<CourseCatalogItem>> GetPublishedCoursesAsync(Guid userId, Guid? languageId, CefrLevel? level, CancellationToken ct);

    /// <summary>Возвращает один опубликовный курс по его читаемому идентификатору вместе с прогрессом пользователя.</summary>
    Task<CourseCatalogItem?> GetPublishedCourseAsync(Guid userId, string slug, CancellationToken ct);

    /// <summary>Определяет идентификатор курса по его slug. Нужен, чтобы отличить «курс не найден» от «уроков нет».</summary>
    Task<Guid?> GetCourseIdBySlugAsync(string slug, CancellationToken ct);

    /// <summary>Возвращает опубликованные уроки курса вместе со статусом прохождения пользователя.</summary>
    Task<CourseLessonsResult> GetCourseLessonsAsync(Guid userId, string slug, CancellationToken ct);

    /// <summary>Возвращает урок по идентификатору вместе с прогрессом пользователя по нему.</summary>
    Task<LessonCatalogItem?> GetLessonAsync(Guid userId, Guid lessonId, CancellationToken ct);

    /// <summary>Возвращает опубликованные темы грамматики с фильтрами по языку и максимальному уровню.</summary>
    Task<IReadOnlyList<GrammarTopicItem>> GetGrammarTopicsAsync(Guid? languageId, CefrLevel? maxLevel, CancellationToken ct);

    /// <summary>Возвращает тему грамматики по идентификатору.</summary>
    Task<GrammarTopicItem?> GetGrammarTopicAsync(Guid id, CancellationToken ct);

    /// <summary>Возвращает все курсы для панели управления, включая неопубликованные.</summary>
    Task<IReadOnlyList<AdminCourseItem>> GetAdminCoursesAsync(Guid? languageId, bool includeUnpublished, CancellationToken ct);

    /// <summary>Возвращает курс для редактирования в панели управления.</summary>
    Task<AdminCourseItem?> GetAdminCourseAsync(Guid id, CancellationToken ct);

    /// <summary>Создаёт курс в каталоге.</summary>
    Task<AdminCourseItem> CreateCourseAsync(Course course, CancellationToken ct);

    /// <summary>
    /// Частично обновляет курс. Перед сохранением проверяет существование языка,
    /// чтобы в базу не попал курс со ссылкой на несуществующий язык.
    /// </summary>
    Task<CourseUpdateResult> UpdateCourseAsync(Guid id, CourseUpdate update, CancellationToken ct);

    /// <summary>Удаляет курс вместе со связанными данными.</summary>
    Task<bool> DeleteCourseAsync(Guid id, CancellationToken ct);

    /// <summary>Проверяет, что курс с указанным идентификатором существует.</summary>
    Task<bool> CourseExistsAsync(Guid courseId, CancellationToken ct);

    /// <summary>Возвращает все уроки курса, включая неопубликованные, — для управления содержимым.</summary>
    Task<IReadOnlyList<AdminLessonItem>> GetAdminCourseLessonsAsync(Guid courseId, CancellationToken ct);

    /// <summary>Возвращает урок для редактирования в панели управления.</summary>
    Task<AdminLessonItem?> GetAdminLessonAsync(Guid id, CancellationToken ct);

    /// <summary>Создаёт урок в указанном курсе.</summary>
    Task<AdminLessonItem> CreateLessonAsync(Lesson lesson, CancellationToken ct);

    /// <summary>Частично обновляет урок.</summary>
    Task<AdminLessonItem?> UpdateLessonAsync(Guid id, LessonUpdate update, CancellationToken ct);

    /// <summary>Удаляет урок из курса.</summary>
    Task<bool> DeleteLessonAsync(Guid id, CancellationToken ct);

    /// <summary>Определяет идентификатор курса урока — нужен для генерации содержимого с опорой на курс.</summary>
    Task<Guid?> GetLessonCourseIdAsync(Guid lessonId, CancellationToken ct);

    /// <summary>Определяет идентификатор языка курса — нужен для генерации структуры курса.</summary>
    Task<Guid?> GetCourseLanguageIdAsync(Guid courseId, CancellationToken ct);
}
