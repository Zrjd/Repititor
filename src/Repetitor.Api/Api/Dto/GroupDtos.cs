using System.ComponentModel.DataAnnotations;

namespace Repetitor.Api.Api.Dto;

/// <summary>Краткая карточка учебной группы в списке учителя.</summary>
public sealed record StudyGroupResponse(
    Guid Id,
    string Name,
    string? Description,
    int MembersCount,
    int CoursesCount,
    bool IsPersonal,
    DateTimeOffset CreatedAt);

/// <summary>Ученик в составе группы.</summary>
public sealed record StudyGroupMemberResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTimeOffset JoinedAt);

/// <summary>Курс, доступный группе.</summary>
public sealed record StudyGroupCourseResponse(
    Guid CourseId,
    string Slug,
    string Title,
    string Level,
    string LanguageCode,
    bool IsPublished,
    bool IsOwnCourse,
    DateTimeOffset AssignedAt);

/// <summary>Код-приглашение в группу.</summary>
public sealed record GroupInvitationResponse(
    Guid Id,
    string Code,
    DateTimeOffset ExpiresAt,
    int MaxUses,
    int UsedCount,
    bool IsRevoked,
    bool IsActive);

/// <summary>Полное состояние группы для кабинета учителя.</summary>
public sealed record StudyGroupDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid TeacherUserId,
    bool IsPersonal,
    DateTimeOffset CreatedAt,
    StudyGroupMemberResponse[] Members,
    StudyGroupCourseResponse[] Courses,
    GroupInvitationResponse[] Invitations);

/// <summary>Группа глазами ученика: учитель и доступные курсы.</summary>
public sealed record LearnerGroupResponse(
    Guid Id,
    string Name,
    string? Description,
    string TeacherDisplayName,
    int MembersCount,
    StudyGroupCourseResponse[] Courses);

/// <summary>Запрос на создание или переименование учебной группы.</summary>
public sealed class StudyGroupRequest
{
    /// <summary>Название группы: для персональных занятий обычно имя ученика.</summary>
    [Required]
    [StringLength(160, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Необязательное описание: цель, расписание, уровень.</summary>
    [StringLength(1000)]
    public string? Description { get; set; }
}

/// <summary>Частичное обновление группы: null означает «поле не передано».</summary>
public sealed class StudyGroupUpdateRequest
{
    [StringLength(160, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
}

/// <summary>Добавление ученика в группу по его email.</summary>
public sealed class AddGroupMemberRequest
{
    /// <summary>Email зарегистрированного ученика; сравнение регистронезависимое.</summary>
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Назначение курса группе.</summary>
public sealed class AssignGroupCourseRequest
{
    /// <summary>Курс: собственный курс учителя или опубликованный системный.</summary>
    [Required]
    public Guid CourseId { get; set; }
}

/// <summary>Параметры нового кода-приглашения.</summary>
public sealed class CreateInvitationRequest
{
    /// <summary>Срок действия кода в днях, от 1 до 365.</summary>
    [Range(1, 365)]
    public int ExpiresInDays { get; set; } = 14;

    /// <summary>Сколько учеников могут вступить по коду; 0 — без ограничения.</summary>
    [Range(0, 1000)]
    public int MaxUses { get; set; }
}

/// <summary>Вступление в группу по коду-приглашению.</summary>
public sealed class JoinGroupRequest
{
    /// <summary>Код-приглашение, выданный учителем.</summary>
    [Required]
    [StringLength(16, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
}
