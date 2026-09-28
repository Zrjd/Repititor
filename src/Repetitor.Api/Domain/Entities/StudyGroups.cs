namespace Repetitor.Api.Domain.Entities;

/// <summary>
/// Учебная группа учителя. Группа из одного ученика — это персональные занятия,
/// поэтому отдельного типа группы нет: всё определяется составом.
/// </summary>
public sealed class StudyGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Учитель, которому принадлежит группа. Только он управляет её составом и курсами.</summary>
    public Guid TeacherUserId { get; set; }
    public User? TeacherUser { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<StudyGroupMember> Members { get; set; } = [];
    public ICollection<StudyGroupCourse> Courses { get; set; } = [];
    public ICollection<StudyGroupInvitation> Invitations { get; set; } = [];
}

/// <summary>
/// Ученик в составе группы. Запись создаётся учителем по email или учеником
/// самостоятельно по коду-приглашению.
/// </summary>
public sealed class StudyGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public StudyGroup? Group { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Курс, доступный группе. Назначение курса сразу зачисляет всех участников группы,
/// а новые участники получают зачисление при добавлении в группу.
/// </summary>
public sealed class StudyGroupCourse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public StudyGroup? Group { get; set; }
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Код-приглашение в группу для будущих учеников. Код конечный, поэтому его можно
/// отозвать, а срок действия и число использований ограничивают рассылку.
/// </summary>
public sealed class StudyGroupInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public StudyGroup? Group { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Максимальное число использований кода; 0 — без ограничений.</summary>
    public int MaxUses { get; set; }

    public int UsedCount { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
