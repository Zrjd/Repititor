using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;

namespace Repetitor.Api.Api.Controllers;

/// <summary>
/// Учебные группы учителя: набор учеников (в том числе группа из одного ученика —
/// персональные занятия) и курсы, доступные участникам группы.
/// Назначение курса сразу зачисляет всех участников, поэтому отдельная запись ученика не нужна.
/// Чужие группы возвращают 403, администратору доступны все.
/// </summary>
[ApiController]
[Route("api/v1/teacher/groups")]
[Authorize(Roles = "Teacher,Admin")]
public sealed class TeacherGroupsController(IGroupDbService groups) : ControllerBase
{
    /// <summary>
    /// Возвращает группы учителя с количеством участников и курсов.
    /// Администратор получает все группы платформы.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(StudyGroupResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<StudyGroupResponse[]>> GetGroups(CancellationToken ct)
    {
        var result = await groups.GetGroupsAsync(Actor(), ct);
        return Ok(result.Select(ToResponse).ToArray());
    }

    /// <summary>
    /// Возвращает курсы, доступные для назначения группе: собственные курсы учителя
    /// (включая черновики) и опубликованные системные курсы.
    /// </summary>
    [HttpGet("assignable-courses")]
    [ProducesResponseType(typeof(AdminCourseResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCourseResponse[]>> GetAssignableCourses(CancellationToken ct)
    {
        var result = await groups.GetAssignableCoursesAsync(Actor(), ct);
        return Ok(result
            .Select(c => new AdminCourseResponse(
                c.Id, c.Slug, c.Title, c.Description, c.Level, c.LanguageId,
                c.LanguageCode, c.CoverUrl, c.AccentColor, c.EstimatedMinutes,
                c.IsPublished, c.SortOrder, c.LessonsCount, c.CreatedAt, c.OwnerDisplayName))
            .ToArray());
    }

    /// <summary>
    /// Возвращает группу с составом, курсами и кодами-приглашениями.
    /// Возвращает 404, если группа не найдена, и 403, если она принадлежит другому учителю.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(StudyGroupDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGroup(Guid id, CancellationToken ct)
    {
        var result = await groups.GetGroupAsync(Actor(), id, ct);
        return Mapped(result, ToDetailResponse);
    }

    /// <summary>
    /// Создаёт новую группу. Группа из одного участника — это персональные занятия.
    /// Возвращает 201 с данными созданной группы.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StudyGroupResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<StudyGroupResponse>> CreateGroup(StudyGroupRequest request, CancellationToken ct)
    {
        var created = await groups.CreateGroupAsync(Actor(), request.Name, request.Description, ct);
        return CreatedAtAction(nameof(GetGroup), new { id = created.Id }, ToResponse(created));
    }

    /// <summary>
    /// Изменяет название и описание группы. Переданные только те поля обновляются.
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(StudyGroupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGroup(
        Guid id, StudyGroupUpdateRequest request, CancellationToken ct)
    {
        var result = await groups.UpdateGroupAsync(
            Actor(), id, new StudyGroupUpdate(request.Name, request.Description), ct);
        return Mapped(result, ToResponse);
    }

    /// <summary>
    /// Удаляет группу вместе с составом, курсами и приглашениями.
    /// Записи учеников на курсы и их прогресс сохраняются.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGroup(Guid id, CancellationToken ct)
    {
        var result = await groups.DeleteGroupAsync(Actor(), id, ct);
        return MappedToNoContent(result);
    }

    /// <summary>
    /// Добавляет в группу зарегистрированного ученика по email и сразу выдаёт ему курсы группы.
    /// Повторное добавление не создаёт дублей. Возвращает 404, если ученик не зарегистрирован.
    /// </summary>
    [HttpPost("{id}/members")]
    [ProducesResponseType(typeof(StudyGroupDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMember(
        Guid id, AddGroupMemberRequest request, CancellationToken ct)
    {
        var result = await groups.AddMemberByEmailAsync(Actor(), id, request.Email, ct);
        return Mapped(result, ToDetailResponse);
    }

    /// <summary>
    /// Исключает ученика из группы. Курсы, которые он уже открыл, остаются у него.
    /// </summary>
    [HttpDelete("{id}/members/{userId}")]
    [ProducesResponseType(typeof(StudyGroupDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(
        Guid id, Guid userId, CancellationToken ct)
    {
        var result = await groups.RemoveMemberAsync(Actor(), id, userId, ct);
        return Mapped(result, ToDetailResponse);
    }

    /// <summary>
    /// Создаёт код-приглашение в группу: им может воспользоваться ученик, который ещё не зачислен.
    /// Срок действия задаётся в днях, лимит использований 0 означает без ограничений.
    /// </summary>
    [HttpPost("{id}/invitations")]
    [ProducesResponseType(typeof(GroupInvitationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInvitation(
        Guid id, CreateInvitationRequest request, CancellationToken ct)
    {
        var result = await groups.CreateInvitationAsync(Actor(), id, request.ExpiresInDays, request.MaxUses, ct);
        return Mapped(result, ToResponse, StatusCodes.Status201Created);
    }

    /// <summary>
    /// Возвращает коды-приглашения группы с их состоянием: активен, отозван или исчерпан.
    /// </summary>
    [HttpGet("{id}/invitations")]
    [ProducesResponseType(typeof(GroupInvitationResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvitations(Guid id, CancellationToken ct)
    {
        var result = await groups.GetInvitationsAsync(Actor(), id, ct);
        return Mapped(result, list => list.Select(ToResponse).ToArray());
    }

    /// <summary>
    /// Отзывает код-приглашение: после отзыва ученик не сможет вступить по нему.
    /// </summary>
    [HttpDelete("{id}/invitations/{invitationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeInvitation(Guid id, Guid invitationId, CancellationToken ct)
    {
        var result = await groups.RevokeInvitationAsync(Actor(), id, invitationId, ct);
        return MappedToNoContent(result);
    }

    /// <summary>
    /// Назначает группе курс и сразу зачисляет всех участников.
    /// Доступны собственные курсы учителя и опубликованные системные курсы;
    /// для остальных возвращается 400.
    /// </summary>
    [HttpPost("{id}/courses")]
    [ProducesResponseType(typeof(StudyGroupDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignCourse(
        Guid id, AssignGroupCourseRequest request, CancellationToken ct)
    {
        var result = await groups.AssignCourseAsync(Actor(), id, request.CourseId, ct);
        return Mapped(result, ToDetailResponse);
    }

    /// <summary>
    /// Убирает курс из группы. Записи участников на курс и их прогресс сохраняются.
    /// </summary>
    [HttpDelete("{id}/courses/{courseId}")]
    [ProducesResponseType(typeof(StudyGroupDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnassignCourse(
        Guid id, Guid courseId, CancellationToken ct)
    {
        var result = await groups.UnassignCourseAsync(Actor(), id, courseId, ct);
        return Mapped(result, ToDetailResponse);
    }

    /// <summary>
    /// Текущий пользователь как актор доступа: слой данных ограничивает учителя его группами.
    /// </summary>
    private DbActor Actor() => new(
        CurrentUserAccessor.GetUserId(User),
        User.IsInRole(nameof(UserRole.Admin)) ? UserRole.Admin : UserRole.Teacher);

    /// <summary>
    /// Преобразует результат операции в HTTP-ответ: 404, 403 или 400 с кодом ошибки.
    /// Формы успеха приводятся к DTO через <paramref name="map"/> и возвращаются с нужным статусом.
    /// </summary>
    private IActionResult Mapped<TIn, TOut>(
        GroupMutationResult<TIn> result,
        Func<TIn, TOut> map,
        int successStatus = StatusCodes.Status200OK) =>
        result.Status switch
        {
            GroupMutationStatus.NotFound => NotFound(),
            GroupMutationStatus.Forbidden => Forbid(),
            GroupMutationStatus.InvalidCourse => InvalidCourse(),
            GroupMutationStatus.InvalidInvitation => InvalidInvitation(),
            _ => StatusCode(successStatus, map(result.Value!))
        };

    private BadRequestObjectResult InvalidCourse() =>
        BadRequest(new ErrorResponse("invalid_course", "Курс недоступен для назначения группе."));

    private BadRequestObjectResult InvalidInvitation() =>
        BadRequest(new ErrorResponse(
            "invalid_invitation", "Код-приглашение недействителен: истёк, отозван или исчерпан."));

    /// <summary>Вариант <see cref="Mapped{TIn, TOut}"/> для операций без тела ответа.</summary>
    private IActionResult MappedToNoContent<T>(GroupMutationResult<T> result) =>
        result.Status switch
        {
            GroupMutationStatus.NotFound => NotFound(),
            GroupMutationStatus.Forbidden => Forbid(),
            GroupMutationStatus.InvalidCourse => InvalidCourse(),
            GroupMutationStatus.InvalidInvitation => InvalidInvitation(),
            _ => NoContent()
        };

    private static StudyGroupResponse ToResponse(StudyGroupItem g) => new(
        g.Id, g.Name, g.Description, g.MembersCount, g.CoursesCount, g.IsPersonal, g.CreatedAt);

    private static StudyGroupDetailResponse ToDetailResponse(StudyGroupDetail g) => new(
        g.Id, g.Name, g.Description, g.TeacherUserId, g.IsPersonal, g.CreatedAt,
        g.Members.Select(m => new StudyGroupMemberResponse(m.UserId, m.DisplayName, m.Email, m.JoinedAt)).ToArray(),
        g.Courses.Select(ToCourseResponse).ToArray(),
        g.Invitations.Select(ToResponse).ToArray());

    private static StudyGroupCourseResponse ToCourseResponse(StudyGroupCourseItem c) => new(
        c.CourseId, c.Slug, c.Title, c.Level, c.LanguageCode, c.IsPublished, c.IsOwnCourse, c.AssignedAt);

    private static GroupInvitationResponse ToResponse(GroupInvitationItem i) => new(
        i.Id, i.Code, i.ExpiresAt, i.MaxUses, i.UsedCount, i.IsRevoked, i.IsActive);
}
