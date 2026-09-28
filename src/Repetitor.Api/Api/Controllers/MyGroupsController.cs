using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.DbServices;

namespace Repetitor.Api.Api.Controllers;

/// <summary>
/// Группы ученика: список групп, в которых он занимается, и вступление по коду-приглашению.
/// Курсы группы выдаются ученику сразу при вступлении, отдельная запись не требуется.
/// </summary>
[ApiController]
[Route("api/v1/groups")]
[Authorize]
public sealed class MyGroupsController(IGroupDbService groups) : ControllerBase
{
    /// <summary>
    /// Возвращает группы, в которых пользователь состоит учеником,
    /// с именем учителя и доступными курсами.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(LearnerGroupResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<LearnerGroupResponse[]>> GetMyGroups(CancellationToken ct)
    {
        var result = await groups.GetLearnerGroupsAsync(CurrentUserAccessor.GetUserId(User), ct);
        return Ok(result.Select(ToResponse).ToArray());
    }

    /// <summary>
    /// Вступает в группу по коду-приглашению и сразу получает курсы группы.
    /// Возвращает 400, если код отозван, истёк или исчерпал лимит использований.
    /// </summary>
    [HttpPost("join")]
    [ProducesResponseType(typeof(LearnerGroupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LearnerGroupResponse>> JoinGroup(JoinGroupRequest request, CancellationToken ct)
    {
        var result = await groups.JoinByCodeAsync(CurrentUserAccessor.GetUserId(User), request.Code, ct);
        return result.Status switch
        {
            GroupMutationStatus.InvalidInvitation => BadRequest(
                new ErrorResponse("invalid_invitation", "Код-приглашение недействителен: истёк, отозван или исчерпан.")),
            _ => Ok(ToResponse(result.Value!))
        };
    }

    private static LearnerGroupResponse ToResponse(LearnerGroupItem g) => new(
        g.Id, g.Name, g.Description, g.TeacherDisplayName, g.MembersCount,
        g.Courses
            .Select(c => new StudyGroupCourseResponse(
                c.CourseId, c.Slug, c.Title, c.Level, c.LanguageCode, c.IsPublished, c.IsOwnCourse, c.AssignedAt))
            .ToArray());
}
