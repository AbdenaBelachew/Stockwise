using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Application.Features.Auth;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Organizations;

public record CreateOrganizationRequest(
    string Name, string? Industry, string Country, string TimeZoneId, string CurrencyCode, string DateFormat);

public record UpdateOrganizationRequest(
    string? Name, string? Industry, string? Country, string? TimeZoneId, string? CurrencyCode, string? DateFormat);

public record InviteMemberRequest(string Email, Guid RoleId);
public record AcceptInvitationRequest(string Token, string FullName, string Password);
public record UpdateMemberRequest(Guid RoleId, MembershipStatus? Status);

public record OrganizationDto(
    Guid Id, string Name, string? Industry, string Country, string TimeZoneId, string CurrencyCode,
    string DateFormat, int ExpiryAlertDays, bool AllowsNegativeStock, bool RequireSeparationOfDuties,
    decimal? ApprovalThresholdAmount, bool AllowOverReceipt, decimal OverReceiptTolerancePercent,
    bool DefaultTrackBatches, bool DefaultTrackExpiry, int MinShelfLifeDays, string Status, DateTime CreatedAt);

public record InvitationDto(Guid Id, string Email, Guid RoleId, string RoleName, string Status, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, string? AcceptToken = null);

public record MemberDto(Guid UserId, string Email, string FullName, Guid RoleId, string RoleName, string Status, DateTime JoinedAtUtc);

public class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.DateFormat).NotEmpty().MaximumLength(40);
    }
}

public class InviteMemberRequestValidator : AbstractValidator<InviteMemberRequest>
{
    public InviteMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class OrganizationService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenantContext;

    public OrganizationService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions,
        ITokenService tokens, IPasswordHasher hasher, IEmailSender email, IAuditService audit, ITenantContext tenantContext)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
        _tokens = tokens; _hasher = hasher; _email = email; _audit = audit; _tenantContext = tenantContext;
    }

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var user = await _db.Users.FirstAsync(u => u.Id == uid, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var org = new Organization
        {
            Name = req.Name.Trim(), Industry = req.Industry, Country = req.Country,
            TimeZoneId = req.TimeZoneId, CurrencyCode = req.CurrencyCode.ToUpperInvariant(),
            DateFormat = req.DateFormat, CreatedBy = uid
        };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);

        var ownerRole = await RoleSeeder.SeedDefaultRolesAsync(_db, org.Id, ct);
        _db.TenantMemberships.Add(new TenantMembership { UserId = uid, OrganizationId = org.Id, RoleId = ownerRole.Id });

        var freePlan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == "FREE", ct);
        if (freePlan != null)
            _db.TenantSubscriptions.Add(new TenantSubscription { OrganizationId = org.Id, PlanId = freePlan.Id });

        foreach (var (name, symbol, fractional) in new[]
                 {
                     ("Piece", "pcs", false), ("Box", "box", false), ("Bottle", "btl", false),
                     ("Kilogram", "kg", true), ("Gram", "g", true), ("Liter", "l", true), ("Meter", "m", true)
                 })
            _db.Units.Add(new UnitOfMeasure { TenantId = org.Id, Name = name, Symbol = symbol, AllowsFractional = fractional });
        _db.Warehouses.Add(new Warehouse { TenantId = org.Id, Name = "Main Warehouse", Code = "MAIN", CreatedBy = uid });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Organization.Created", "Organization", org.Id, new { org.Name }, ct);
        await tx.CommitAsync(ct);
        return ToDto(org);
    }

    public async Task<OrganizationDto> GetCurrentAsync(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == tenantId, ct)
            ?? throw AppException.NotFound("Organization");
        return ToDto(org);
    }

    public async Task<OrganizationDto> UpdateCurrentAsync(UpdateOrganizationRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var tenantId = _currentUser.TenantId!.Value;
        await _permissions.EnsurePermissionAsync(uid, tenantId, Permissions.OrgSettingsManage, ct);

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == tenantId, ct)
            ?? throw AppException.NotFound("Organization");

        if (req.Name is { } n) org.Name = n.Trim();
        if (req.Industry is { } i) org.Industry = i;
        if (req.Country is { } c) org.Country = c;
        if (req.TimeZoneId is { } tz) org.TimeZoneId = tz;
        if (req.CurrencyCode is { } cur) org.CurrencyCode = cur.ToUpperInvariant();
        if (req.DateFormat is { } df) org.DateFormat = df;
        org.UpdatedAt = DateTime.UtcNow;
        org.UpdatedBy = uid;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Organization.Updated", "Organization", org.Id, req, ct);
        return ToDto(org);
    }

    // ---- Team management ----

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        return await (from m in _db.TenantMemberships.AsNoTracking()
                      join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                      join r in _db.Roles.AsNoTracking() on m.RoleId equals r.Id
                      where m.OrganizationId == tenantId
                      orderby m.CreatedAtUtc
                      select new MemberDto(u.Id, u.Email, u.FullName, r.Id, r.Name, m.Status.ToString(), m.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description,
                r.Permissions.Select(p => p.Permission).ToList()))
            .ToListAsync(ct);
        return roles;
    }

    public async Task<InvitationDto> InviteAsync(InviteMemberRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var tenantId = _currentUser.TenantId!.Value;
        await _permissions.EnsurePermissionAsync(uid, tenantId, Permissions.UsersManage, ct);

        var email = req.Email.Trim().ToLowerInvariant();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.RoleId && r.TenantId == tenantId, ct)
            ?? throw AppException.NotFound("Role");

        var existingMember = await (from m in _db.TenantMemberships.AsNoTracking()
                                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                                    where m.OrganizationId == tenantId && u.Email == email
                                    select m).AnyAsync(ct);
        if (existingMember)
            throw AppException.Conflict("This person is already a member of the organization.");

        var pending = await _db.Invitations
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email == email && i.Status == InvitationStatus.Pending, ct);

        string rawToken = _tokens.NewOpaqueToken();
        if (pending != null)
        {
            pending.RoleId = role.Id;
            pending.ExpiresAtUtc = DateTime.UtcNow.AddDays(7);
            pending.InvitedByUserId = uid;
            pending.TokenHash = _tokens.HashToken(rawToken);
        }
        else
        {
            pending = new Invitation
            {
                TenantId = tenantId, Email = email, RoleId = role.Id,
                TokenHash = _tokens.HashToken(rawToken),
                InvitedByUserId = uid, ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            };
            _db.Invitations.Add(pending);
        }
        await _db.SaveChangesAsync(ct);

        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);
        await _email.SendAsync(email, $"You've been invited to {org.Name} on StockWise",
            $"You have been invited to join {org.Name} as {role.Name}. " +
            $"Accept this invitation with token: {rawToken}", ct);

        await _audit.WriteAsync("Team.Invited", "Invitation", pending.Id, new { email, role = role.Name }, ct);
        return ToInvitationDto(pending, role.Name, rawToken);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        return await (from i in _db.Invitations.AsNoTracking()
                      join r in _db.Roles.AsNoTracking() on i.RoleId equals r.Id
                      where i.TenantId == tenantId && i.Status == InvitationStatus.Pending
                      orderby i.CreatedAtUtc descending
                      select new InvitationDto(i.Id, i.Email, i.RoleId, r.Name, i.Status.ToString(),
                          i.CreatedAtUtc, i.ExpiresAtUtc)).ToListAsync(ct);
    }

    public async Task RevokeInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var tenantId = _currentUser.TenantId!.Value;
        await _permissions.EnsurePermissionAsync(uid, tenantId, Permissions.UsersManage, ct);
        var inv = await _db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.TenantId == tenantId, ct)
            ?? throw AppException.NotFound("Invitation");
        inv.Status = InvitationStatus.Revoked;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Team.InvitationRevoked", "Invitation", inv.Id, null, ct);
    }

    /// <summary>Accepts a pending invitation: links the signed-in user, or creates an account when token+password are supplied.</summary>
    public async Task<AuthResponse?> AcceptInvitationAsync(AcceptInvitationRequest req, string ip, CancellationToken ct)
    {
        var hash = _tokens.HashToken(req.Token);
        // Load without tenant filter: the accepter may not yet belong to the tenant.
        var inv = await _db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null || inv.Status != InvitationStatus.Pending || inv.ExpiresAtUtc < DateTime.UtcNow)
            throw AppException.BadRequest("This invitation is invalid, expired, or already used.");

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == inv.TenantId && o.Status == OrganizationStatus.Active, ct)
            ?? throw AppException.BadRequest("The organization for this invitation is no longer active.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == inv.Email, ct);
        if (user == null)
        {
            user = new User
            {
                Email = inv.Email, FullName = req.FullName.Trim(), PasswordHash = _hasher.Hash(req.Password)
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }

        var alreadyMember = await _db.TenantMemberships
            .AnyAsync(m => m.UserId == user.Id && m.OrganizationId == inv.TenantId, ct);
        if (alreadyMember)
            throw AppException.Conflict("You are already a member of this organization.");

        _db.TenantMemberships.Add(new TenantMembership
        {
            UserId = user.Id, OrganizationId = inv.TenantId, RoleId = inv.RoleId
        });
        inv.Status = InvitationStatus.Accepted;
        inv.AcceptedByUserId = user.Id;
        inv.AcceptedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Team.InvitationAccepted", "Invitation", inv.Id, new { user.Email }, ct);
        return await IssueAuthAsync(user, org, ip, ct);
    }

    public async Task UpdateMemberAsync(Guid userId, UpdateMemberRequest req, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? throw AppException.Unauthorized();
        var tenantId = _currentUser.TenantId!.Value;
        await _permissions.EnsurePermissionAsync(actorId, tenantId, Permissions.UsersManage, ct);

        var membership = await _db.TenantMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == tenantId, ct)
            ?? throw AppException.NotFound("Member");

        if (req.RoleId != Guid.Empty)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.RoleId && r.TenantId == tenantId, ct)
                ?? throw AppException.NotFound("Role");
            membership.RoleId = role.Id;
        }
        if (req.Status is { } status)
        {
            if (userId == actorId && status == MembershipStatus.Disabled)
                throw AppException.BadRequest("You cannot disable your own account.");
            membership.Status = status;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Team.MemberUpdated", "TenantMembership", membership.Id, req, ct);
    }

    private async Task<AuthResponse> IssueAuthAsync(User user, Organization org, string ip, CancellationToken ct)
    {
        _tenantContext.SetTenant(org.Id);
        var (access, expires) = _tokens.CreateAccessToken(user, org.Id, org.Name);
        var refreshRaw = _tokens.NewOpaqueToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = _tokens.HashToken(refreshRaw),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7), CreatedByIp = string.IsNullOrEmpty(ip) ? null : ip
        });
        await _db.SaveChangesAsync(ct);
        var perms = await _permissions.GetPermissionsAsync(user.Id, org.Id, ct);
        return new AuthResponse(access, expires,
            new UserDto(user.Id, user.Email, user.FullName, user.EmailVerified), org.Id, org.Name, [.. perms]);
    }

    public record RoleDto(Guid Id, string Name, string Description, IReadOnlyList<string> Permissions);

    internal static OrganizationDto ToDto(Organization o) => new(
        o.Id, o.Name, o.Industry, o.Country, o.TimeZoneId, o.CurrencyCode, o.DateFormat,
        o.ExpiryAlertDays, o.AllowsNegativeStock, o.RequireSeparationOfDuties, o.ApprovalThresholdAmount,
        o.AllowOverReceipt, o.OverReceiptTolerancePercent, o.DefaultTrackBatches, o.DefaultTrackExpiry,
        o.MinShelfLifeDays, o.Status.ToString(), o.CreatedAt);

    internal static InvitationDto ToInvitationDto(Invitation i, string roleName, string? acceptToken = null) =>
        new(i.Id, i.Email, i.RoleId, roleName, i.Status.ToString(), i.CreatedAtUtc, i.ExpiresAtUtc, acceptToken);
}
