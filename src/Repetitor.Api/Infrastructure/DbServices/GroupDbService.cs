using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.DbServices;

/// <summary>
/// Реализация <see cref="IGroupDbService"/> поверх фабрики контекстов EF Core.
/// Группа доступна для управления только её учителю (или администратору), а ученик
/// попадает в неё либо по email, либо по коду-приглашению.
/// </summary>
public sealed class GroupDbService(IDbContextFactory<AppDbContext> dbFactory) : IGroupDbService
{
    public async Task<IReadOnlyList<StudyGroupItem>> GetGroupsAsync(DbActor actor, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.StudyGroups
            .Include(g => g.Members)
            .Include(g => g.Courses)
            .AsNoTracking()
            .AsQueryable();

        if (!IsAdmin(actor))
        {
            query = query.Where(g => g.TeacherUserId == actor.UserId);
        }

        var groups = await query
            .OrderBy(g => g.Name)
            .ThenBy(g => g.CreatedAt)
            .ToListAsync(ct);

        return groups.Select(MapGroup).ToArray();
    }

    public async Task<GroupMutationResult<StudyGroupDetail>> GetGroupAsync(
        DbActor actor,
        Guid id,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadDetailAsync(db, actor, id, ct);
    }

    public async Task<StudyGroupItem> CreateGroupAsync(
        DbActor actor,
        string name,
        string? description,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = new StudyGroup
        {
            TeacherUserId = actor.UserId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };

        db.StudyGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return MapGroup(group);
    }

    public async Task<GroupMutationResult<StudyGroupItem>> UpdateGroupAsync(
        DbActor actor,
        Guid id,
        StudyGroupUpdate update,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return GroupMutationResult<StudyGroupItem>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<StudyGroupItem>.Forbidden();
        }

        if (update.Name is { } name) group.Name = name.Trim();
        if (update.Description is { } description)
        {
            group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        await db.SaveChangesAsync(ct);
        return GroupMutationResult<StudyGroupItem>.Ok(MapGroup(group));
    }

    public async Task<GroupMutationResult<bool>> DeleteGroupAsync(
        DbActor actor,
        Guid id,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return GroupMutationResult<bool>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<bool>.Forbidden();
        }

        db.StudyGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return GroupMutationResult<bool>.Ok(true);
    }

    public async Task<GroupMutationResult<StudyGroupDetail>> AddMemberByEmailAsync(
        DbActor actor,
        Guid groupId,
        string email,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<StudyGroupDetail>.Forbidden();
        }

        var normalized = email.Trim().ToLowerInvariant();
        var userId = await db.Users
            .Where(u => u.Email == normalized)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (userId == Guid.Empty)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        var alreadyMember = await db.StudyGroupMembers
            .AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct);

        if (!alreadyMember)
        {
            db.StudyGroupMembers.Add(new StudyGroupMember { GroupId = groupId, UserId = userId });
            await EnrollOnGroupCoursesAsync(db, groupId, [userId], ct);
            await db.SaveChangesAsync(ct);
        }

        return await LoadDetailAsync(db, actor, groupId, ct);
    }

    public async Task<GroupMutationResult<StudyGroupDetail>> RemoveMemberAsync(
        DbActor actor,
        Guid groupId,
        Guid userId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<StudyGroupDetail>.Forbidden();
        }

        var member = await db.StudyGroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        db.StudyGroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return await LoadDetailAsync(db, actor, groupId, ct);
    }

    public async Task<GroupMutationResult<GroupInvitationItem>> CreateInvitationAsync(
        DbActor actor,
        Guid groupId,
        int expiresInDays,
        int maxUses,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<GroupInvitationItem>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<GroupInvitationItem>.Forbidden();
        }

        var invitation = new StudyGroupInvitation
        {
            GroupId = groupId,
            Code = await GenerateCodeAsync(db, ct),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(Math.Clamp(expiresInDays, 1, 365)),
            MaxUses = Math.Clamp(maxUses, 0, 1000)
        };

        db.StudyGroupInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return GroupMutationResult<GroupInvitationItem>.Ok(MapInvitation(invitation, DateTimeOffset.UtcNow));
    }

    public async Task<GroupMutationResult<IReadOnlyList<GroupInvitationItem>>> GetInvitationsAsync(
        DbActor actor,
        Guid groupId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<IReadOnlyList<GroupInvitationItem>>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<IReadOnlyList<GroupInvitationItem>>.Forbidden();
        }

        var now = DateTimeOffset.UtcNow;
        var invitations = await db.StudyGroupInvitations
            .AsNoTracking()
            .Where(i => i.GroupId == groupId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return GroupMutationResult<IReadOnlyList<GroupInvitationItem>>.Ok(
            invitations.Select(i => MapInvitation(i, now)).ToArray());
    }

    public async Task<GroupMutationResult<bool>> RevokeInvitationAsync(
        DbActor actor,
        Guid groupId,
        Guid invitationId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<bool>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<bool>.Forbidden();
        }

        var invitation = await db.StudyGroupInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.GroupId == groupId, ct);
        if (invitation is null)
        {
            return GroupMutationResult<bool>.NotFound();
        }

        invitation.IsRevoked = true;
        await db.SaveChangesAsync(ct);
        return GroupMutationResult<bool>.Ok(true);
    }

    public async Task<GroupMutationResult<StudyGroupDetail>> AssignCourseAsync(
        DbActor actor,
        Guid groupId,
        Guid courseId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<StudyGroupDetail>.Forbidden();
        }

        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null || !IsAssignableCourse(actor, course))
        {
            return GroupMutationResult<StudyGroupDetail>.InvalidCourse();
        }

        var alreadyAssigned = await db.StudyGroupCourses
            .AnyAsync(gc => gc.GroupId == groupId && gc.CourseId == courseId, ct);

        if (!alreadyAssigned)
        {
            db.StudyGroupCourses.Add(new StudyGroupCourse { GroupId = groupId, CourseId = courseId });
            var memberIds = await db.StudyGroupMembers
                .Where(m => m.GroupId == groupId)
                .Select(m => m.UserId)
                .ToListAsync(ct);
            await EnrollOnGroupCoursesAsync(db, groupId, memberIds, ct);
            await db.SaveChangesAsync(ct);
        }

        return await LoadDetailAsync(db, actor, groupId, ct);
    }

    public async Task<GroupMutationResult<StudyGroupDetail>> UnassignCourseAsync(
        DbActor actor,
        Guid groupId,
        Guid courseId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<StudyGroupDetail>.Forbidden();
        }

        var assignment = await db.StudyGroupCourses
            .FirstOrDefaultAsync(gc => gc.GroupId == groupId && gc.CourseId == courseId, ct);
        if (assignment is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        db.StudyGroupCourses.Remove(assignment);
        await db.SaveChangesAsync(ct);
        return await LoadDetailAsync(db, actor, groupId, ct);
    }

    public async Task<IReadOnlyList<AdminCourseItem>> GetAssignableCoursesAsync(
        DbActor actor,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Свои курсы доступны учителю в любом состоянии, системные — только опубликованные.
        var query = IsAdmin(actor)
            ? db.Courses
                .Include(c => c.Language)
                .Include(c => c.Owner)
                .Include(c => c.Lessons)
                .AsNoTracking()
            : db.Courses
                .Include(c => c.Language)
                .Include(c => c.Owner)
                .Include(c => c.Lessons)
                .AsNoTracking()
                .Where(c => c.OwnerUserId == actor.UserId || (c.OwnerUserId == null && c.IsPublished));

        var courses = await query
            .OrderBy(c => c.Language!.SortOrder)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Title)
            .ToListAsync(ct);

        return courses
            .Select(c => new AdminCourseItem(
                c.Id,
                c.Slug,
                c.Title,
                c.Description,
                c.Level.ToString(),
                c.LanguageId,
                c.Language?.Code ?? string.Empty,
                c.CoverUrl,
                c.AccentColor,
                c.EstimatedMinutes,
                c.IsPublished,
                c.SortOrder,
                c.Lessons.Count,
                c.CreatedAt,
                c.OwnerUserId,
                c.Owner?.DisplayName))
            .ToArray();
    }

    public async Task<IReadOnlyList<LearnerGroupItem>> GetLearnerGroupsAsync(
        Guid userId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.StudyGroups
            .Include(g => g.TeacherUser)
            .Include(g => g.Members)
            .Include(g => g.Courses).ThenInclude(gc => gc.Course).ThenInclude(c => c!.Language)
            .AsNoTracking()
            .Where(g => g.Members.Any(m => m.UserId == userId))
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return groups.Select(MapLearnerGroup).ToArray();
    }

    public async Task<GroupMutationResult<LearnerGroupItem>> JoinByCodeAsync(
        Guid userId,
        string code,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var normalized = code.Trim().ToUpperInvariant();

        var invitation = await db.StudyGroupInvitations
            .FirstOrDefaultAsync(i => i.Code == normalized, ct);

        if (invitation is null || !IsInvitationUsable(invitation, now))
        {
            return GroupMutationResult<LearnerGroupItem>.InvalidInvitation();
        }

        var alreadyMember = await db.StudyGroupMembers
            .AnyAsync(m => m.GroupId == invitation.GroupId && m.UserId == userId, ct);

        if (!alreadyMember)
        {
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = invitation.GroupId,
                UserId = userId
            });
            await EnrollOnGroupCoursesAsync(db, invitation.GroupId, [userId], ct);
        }

        invitation.UsedCount++;
        await db.SaveChangesAsync(ct);

        var group = await db.StudyGroups
            .Include(g => g.TeacherUser)
            .Include(g => g.Members)
            .Include(g => g.Courses).ThenInclude(gc => gc.Course).ThenInclude(c => c!.Language)
            .AsNoTracking()
            .FirstAsync(g => g.Id == invitation.GroupId, ct);

        return GroupMutationResult<LearnerGroupItem>.Ok(MapLearnerGroup(group));
    }

    /// <summary>
    /// Создаёт записи на все курсы группы для перечисленных учеников, пропуская тех,
    /// кто уже зачислен: повторное назначение курса не должно дублировать прогресс.
    /// </summary>
    private static async Task EnrollOnGroupCoursesAsync(
        AppDbContext db,
        Guid groupId,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        var courseIds = await db.StudyGroupCourses
            .Where(gc => gc.GroupId == groupId)
            .Select(gc => gc.CourseId)
            .ToListAsync(ct);

        if (courseIds.Count == 0)
        {
            return;
        }

        var existing = await db.Enrollments
            .Where(e => userIds.Contains(e.UserId) && courseIds.Contains(e.CourseId))
            .Select(e => new { e.UserId, e.CourseId })
            .ToListAsync(ct);

        var enrolled = existing.Select(e => (e.UserId, e.CourseId)).ToHashSet();
        foreach (var userId in userIds)
        {
            foreach (var courseId in courseIds)
            {
                if (enrolled.Contains((userId, courseId)))
                {
                    continue;
                }

                db.Enrollments.Add(new CourseEnrollment { UserId = userId, CourseId = courseId });
            }
        }
    }

    private static async Task<GroupMutationResult<StudyGroupDetail>> LoadDetailAsync(
        AppDbContext db,
        DbActor actor,
        Guid groupId,
        CancellationToken ct)
    {
        var group = await db.StudyGroups
            .Include(g => g.Members).ThenInclude(m => m.User)
            .Include(g => g.Courses).ThenInclude(gc => gc.Course).ThenInclude(c => c!.Language)
            .Include(g => g.Invitations)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);

        if (group is null)
        {
            return GroupMutationResult<StudyGroupDetail>.NotFound();
        }

        if (!CanManageGroup(actor, group))
        {
            return GroupMutationResult<StudyGroupDetail>.Forbidden();
        }

        return GroupMutationResult<StudyGroupDetail>.Ok(MapDetail(group, actor.UserId));
    }

    /// <summary>
    /// Генерирует короткий код-приглашение, ещё не встречающийся в базе.
    /// Буквы и цифры без неоднозначных символов (0/O, 1/I), чтобы код было легко продиктовать.
    /// </summary>
    private static async Task<string> GenerateCodeAsync(AppDbContext db, CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = RandomNumberGenerator.GetString(alphabet, 8);
            if (!await db.StudyGroupInvitations.AnyAsync(i => i.Code == code, ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Не удалось сгенерировать уникальный код-приглашение.");
    }

    private static bool IsInvitationUsable(StudyGroupInvitation invitation, DateTimeOffset now) =>
        !invitation.IsRevoked
        && invitation.ExpiresAt > now
        && (invitation.MaxUses == 0 || invitation.UsedCount < invitation.MaxUses);

    /// <summary>
    /// Курс можно назначить группе, если он принадлежит учителю или является
    /// опубликованным системным курсом: черновики других учителей закрыты.
    /// </summary>
    private static bool IsAssignableCourse(DbActor actor, Course course) =>
        course.OwnerUserId == actor.UserId || (course.OwnerUserId is null && course.IsPublished);

    private static bool IsAdmin(DbActor actor) => actor.Role == UserRole.Admin;

    private static bool CanManageGroup(DbActor actor, StudyGroup group) =>
        IsAdmin(actor) || group.TeacherUserId == actor.UserId;

    private static StudyGroupItem MapGroup(StudyGroup group) => new(
        group.Id,
        group.Name,
        group.Description,
        group.Members.Count,
        group.Courses.Count,
        group.Members.Count == 1,
        group.CreatedAt);

    private static StudyGroupDetail MapDetail(StudyGroup group, Guid viewerUserId)
    {
        var now = DateTimeOffset.UtcNow;
        return new StudyGroupDetail(
            group.Id,
            group.Name,
            group.Description,
            group.TeacherUserId,
            group.Members.Count == 1,
            group.CreatedAt,
            group.Members
                .OrderBy(m => m.JoinedAt)
                .Select(m => new StudyGroupMemberItem(m.UserId, m.User?.DisplayName ?? string.Empty, m.User?.Email ?? string.Empty, m.JoinedAt))
                .ToArray(),
            group.Courses
                .OrderBy(gc => gc.Course?.SortOrder ?? 0)
                .Select(gc => MapGroupCourse(gc, viewerUserId))
                .ToArray(),
            group.Invitations
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => MapInvitation(i, now))
                .ToArray());
    }

    /// <summary>
    /// Курс в составе группы. <paramref name="viewerUserId"/> определяет, помечается ли курс
    /// как собственный: ученик не владеет курсами, поэтому для него признак всегда false.
    /// </summary>
    private static StudyGroupCourseItem MapGroupCourse(StudyGroupCourse assignment, Guid viewerUserId) => new(
        assignment.CourseId,
        assignment.Course?.Slug ?? string.Empty,
        assignment.Course?.Title ?? string.Empty,
        assignment.Course?.Level.ToString() ?? string.Empty,
        assignment.Course?.Language?.Code ?? string.Empty,
        assignment.Course?.IsPublished ?? false,
        assignment.Course?.OwnerUserId == viewerUserId,
        assignment.AssignedAt);

    private static GroupInvitationItem MapInvitation(StudyGroupInvitation invitation, DateTimeOffset now) => new(
        invitation.Id,
        invitation.Code,
        invitation.ExpiresAt,
        invitation.MaxUses,
        invitation.UsedCount,
        invitation.IsRevoked,
        IsInvitationUsable(invitation, now));

    private static LearnerGroupItem MapLearnerGroup(StudyGroup group) => new(
        group.Id,
        group.Name,
        group.Description,
        group.TeacherUser?.DisplayName ?? string.Empty,
        group.Members.Count,
        group.Courses
            .OrderBy(gc => gc.Course?.SortOrder ?? 0)
            .Select(gc => MapGroupCourse(gc, Guid.Empty))
            .ToArray());
}
