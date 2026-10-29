using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockWise.Api.Infrastructure;
using StockWise.Application.Common;
using StockWise.Application.Features.Auth;
using StockWise.Application.Features.Insights;
using StockWise.Application.Features.Organizations;
using StockWise.Domain.Enums;

namespace StockWise.Api.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly OrganizationService _orgs;

    public OrganizationsController(OrganizationService orgs) => _orgs = orgs;

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest req, CancellationToken ct)
        => Ok(await _orgs.CreateAsync(req, ct));

    [HttpGet("current")]
    public async Task<ActionResult<OrganizationDto>> GetCurrent(CancellationToken ct)
        => Ok(await _orgs.GetCurrentAsync(ct));

    [HttpPatch("current")]
    public async Task<ActionResult<OrganizationDto>> UpdateCurrent(UpdateOrganizationRequest req, CancellationToken ct)
        => Ok(await _orgs.UpdateCurrentAsync(req, ct));

    // ---- Team ----

    [HttpGet("current/members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> ListMembers(CancellationToken ct)
        => Ok(await _orgs.ListMembersAsync(ct));

    [HttpGet("current/roles")]
    public async Task<ActionResult<IReadOnlyList<OrganizationService.RoleDto>>> ListRoles(CancellationToken ct)
        => Ok(await _orgs.ListRolesAsync(ct));

    [HttpPatch("current/members/{userId:guid}")]
    public async Task<IActionResult> UpdateMember(Guid userId, UpdateMemberRequest req, CancellationToken ct)
    {
        await _orgs.UpdateMemberAsync(userId, req, ct);
        return NoContent();
    }

    [HttpPost("current/invitations")]
    [AuthorizePermission(Permissions.UsersManage)]
    public async Task<ActionResult<InvitationDto>> Invite(InviteMemberRequest req, CancellationToken ct)
        => Ok(await _orgs.InviteAsync(req, ct));

    [HttpGet("current/invitations")]
    public async Task<ActionResult<IReadOnlyList<InvitationDto>>> ListInvitations(CancellationToken ct)
        => Ok(await _orgs.ListInvitationsAsync(ct));

    [HttpDelete("current/invitations/{id:guid}")]
    [AuthorizePermission(Permissions.UsersManage)]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct)
    {
        await _orgs.RevokeInvitationAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/invitations")]
public class InvitationsController : ControllerBase
{
    private readonly OrganizationService _orgs;

    public InvitationsController(OrganizationService orgs) => _orgs = orgs;

    /// <summary>Accepts an invitation. Creates the account when the email is new.</summary>
    [HttpPost("accept")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Accept(AcceptInvitationRequest req, CancellationToken ct)
    {
        var res = await _orgs.AcceptInvitationAsync(req, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", ct);
        return Ok(res);
    }
}

[ApiController]
[Route("api/v1/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;

    public SettingsController(SettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct) => Ok(await _settings.GetAsync(ct));

    [HttpPatch]
    public async Task<ActionResult<SettingsDto>> Update(UpdateSettingsRequest req, CancellationToken ct)
        => Ok(await _settings.UpdateAsync(req, ct));
}

[ApiController]
[Route("api/v1/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly AuditQueryService _audit;

    public AuditLogsController(AuditQueryService audit) => _audit = audit;

    [HttpGet]
    [AuthorizePermission(Permissions.AuditLogsRead)]
    public async Task<ActionResult<PageResult<AuditLogDto>>> List(
        [FromQuery] string? action, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _audit.ListAsync(action, search, page, pageSize, ct));
}
