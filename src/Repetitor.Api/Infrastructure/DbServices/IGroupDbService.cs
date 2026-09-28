using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Операции учителя над учебными группами: состав, коды-приглашения и назначение курсов.
/// Назначение курса сразу зачисляет участников группы, поэтому доступ к курсу появляется без действий ученика.
/// </summary>
public interface IGroupDbService
{
    /// <summary>Возвращает группы учителя; администратор видит все группы.</summary>
    Task<IReadOnlyList<StudyGroupItem>> GetGroupsAsync(DbActor actor, CancellationToken ct);

    /// <summary>Возвращает группу с составом, курсами и кодами-приглашениями либо 403/404.</summary>
    Task<GroupMutationResult<StudyGroupDetail>> GetGroupAsync(DbActor actor, Guid id, CancellationToken ct);

    /// <summary>Создаёт группу, принадлежащую текущему учителю.</summary>
    Task<StudyGroupItem> CreateGroupAsync(DbActor actor, string name, string? description, CancellationToken ct);

    /// <summary>Изменяет название и описание группы.</summary>
    Task<GroupMutationResult<StudyGroupItem>> UpdateGroupAsync(
        DbActor actor, Guid id, StudyGroupUpdate update, CancellationToken ct);

    /// <summary>
    /// Удаляет группу вместе с составом, курсами и приглашениями.
    /// Записи на курсы у учеников сохраняются, чтобы не терять их прогресс.
    /// </summary>
    Task<GroupMutationResult<bool>> DeleteGroupAsync(DbActor actor, Guid id, CancellationToken ct);

    /// <summary>Добавляет в группу зарегистрированного ученика по email и выдаёт ему курсы группы.</summary>
    Task<GroupMutationResult<StudyGroupDetail>> AddMemberByEmailAsync(
        DbActor actor, Guid groupId, string email, CancellationToken ct);

    /// <summary>Исключает ученика из группы; выданные им записи на курсы сохраняются.</summary>
    Task<GroupMutationResult<StudyGroupDetail>> RemoveMemberAsync(
        DbActor actor, Guid groupId, Guid userId, CancellationToken ct);

    /// <summary>Создаёт код-приглашение в группу со сроком действия и лимитом использований.</summary>
    Task<GroupMutationResult<GroupInvitationItem>> CreateInvitationAsync(
        DbActor actor, Guid groupId, int expiresInDays, int maxUses, CancellationToken ct);

    /// <summary>Возвращает коды-приглашения группы с их текущим состоянием.</summary>
    Task<GroupMutationResult<IReadOnlyList<GroupInvitationItem>>> GetInvitationsAsync(
        DbActor actor, Guid groupId, CancellationToken ct);

    /// <summary>Отзывает код-приглашение: после этого ученик не сможет вступить по нему.</summary>
    Task<GroupMutationResult<bool>> RevokeInvitationAsync(
        DbActor actor, Guid groupId, Guid invitationId, CancellationToken ct);

    /// <summary>
    /// Назначает группе курс и сразу зачисляет всех участников.
    /// Доступны собственные курсы учителя и опубликованные системные курсы.
    /// </summary>
    Task<GroupMutationResult<StudyGroupDetail>> AssignCourseAsync(
        DbActor actor, Guid groupId, Guid courseId, CancellationToken ct);

    /// <summary>Убирает курс из группы; записи участников на курс сохраняются.</summary>
    Task<GroupMutationResult<StudyGroupDetail>> UnassignCourseAsync(
        DbActor actor, Guid groupId, Guid courseId, CancellationToken ct);

    /// <summary>Возвращает курсы, которые учитель может назначить группе: свои и опубликованные системные.</summary>
    Task<IReadOnlyList<AdminCourseItem>> GetAssignableCoursesAsync(DbActor actor, CancellationToken ct);

    /// <summary>Возвращает группы, в которых пользователь состоит учеником.</summary>
    Task<IReadOnlyList<LearnerGroupItem>> GetLearnerGroupsAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Вступает в группу по коду-приглашению и зачисляет на курсы группы.
    /// Возвращает 400, если код отозван, истёк или исчерпал лимит использований.
    /// </summary>
    Task<GroupMutationResult<LearnerGroupItem>> JoinByCodeAsync(Guid userId, string code, CancellationToken ct);
}
