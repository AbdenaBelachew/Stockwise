using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Auth;

public record RegisterRequest(
    string FullName, string Email, string Password,
    string OrganizationName, string? Industry, string Country,
    string TimeZoneId, string CurrencyCode, string DateFormat);

public record LoginRequest(string Email, string Password);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UpdateProfileRequest(string FullName);
public record VerifyEmailRequest(string Token);
public record SwitchOrganizationRequest(Guid OrganizationId);

public record UserDto(Guid Id, string Email, string FullName, bool EmailVerified);

public record AuthResponse(
    string AccessToken, DateTime AccessTokenExpiresAtUtc, UserDto User,
    Guid OrganizationId, string OrganizationName, string[] Permissions)
{
    /// <summary>Transient: transported only via HttpOnly cookie by the API layer; never serialized to JSON.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RefreshToken { get; init; }
}

public record OrganizationSummaryDto(Guid Id, string Name, string Role);

public record MeResponse(
    UserDto User, Guid ActiveOrganizationId, string ActiveOrganizationName,
    IReadOnlyList<OrganizationSummaryDto> Organizations, string[] Permissions);

public record AuthTokens(string AccessToken, DateTime ExpiresAtUtc);

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.DateFormat).NotEmpty().MaximumLength(40);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public static class RoleSeeder
{
    /// <summary>Creates the default system roles (with their permission sets) for a new organization.</summary>
    public static async Task<Role> SeedDefaultRolesAsync(IAppDbContext db, Guid tenantId, CancellationToken ct)
    {
        Role ownerRole = null!;
        foreach (var (name, perms) in Permissions.RoleTemplates)
        {
            var role = new Role
            {
                TenantId = tenantId, Name = name, IsSystem = true,
                Description = $"Default {name} role.",
                Permissions = perms.Select(p => new RolePermission { Permission = p }).ToList()
            };
            db.Roles.Add(role);
            if (name == "Business Owner") ownerRole = role;
        }
        await db.SaveChangesAsync(ct);
        return ownerRole;
    }
}

public class AuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenantContext;

    public AuthService(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, IEmailSender email,
        ICurrentUser currentUser, IPermissionChecker permissions, IAuditService audit, ITenantContext tenantContext)
    {
        _db = db; _hasher = hasher; _tokens = tokens; _email = email;
        _currentUser = currentUser; _permissions = permissions; _audit = audit; _tenantContext = tenantContext;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw AppException.Conflict("An account with this email already exists.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var user = new User
        {
            Email = email,
            FullName = req.FullName.Trim(),
            PasswordHash = _hasher.Hash(req.Password),
            EmailVerificationTokenHash = _tokens.HashToken(_tokens.NewOpaqueToken())
        };
        _db.Users.Add(user);

        var org = new Organization
        {
            Name = req.OrganizationName.Trim(),
            Industry = req.Industry,
            Country = req.Country,
            TimeZoneId = req.TimeZoneId,
            CurrencyCode = req.CurrencyCode.ToUpperInvariant(),
            DateFormat = req.DateFormat,
            CreatedBy = user.Id
        };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);

        var ownerRole = await RoleSeeder.SeedDefaultRolesAsync(_db, org.Id, ct);
        _db.TenantMemberships.Add(new TenantMembership
        {
            UserId = user.Id, OrganizationId = org.Id, RoleId = ownerRole.Id
        });

        var freePlan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == "FREE", ct);
        if (freePlan != null)
            _db.TenantSubscriptions.Add(new TenantSubscription { OrganizationId = org.Id, PlanId = freePlan.Id });

        // Smooth onboarding: default units and a main warehouse.
        foreach (var (name, symbol, fractional) in new[]
                 {
                     ("Piece", "pcs", false), ("Box", "box", false), ("Bottle", "btl", false),
                     ("Kilogram", "kg", true), ("Gram", "g", true), ("Liter", "l", true), ("Meter", "m", true)
                 })
            _db.Units.Add(new UnitOfMeasure { TenantId = org.Id, Name = name, Symbol = symbol, AllowsFractional = fractional });

        _db.Warehouses.Add(new Warehouse
        {
            TenantId = org.Id, Name = "Main Warehouse", Code = "MAIN", IsActive = true, CreatedBy = user.Id
        });

        await _db.SaveChangesAsync(ct);

        var verifyUrl = $"/verify-email?token={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user.EmailVerificationTokenHash))}";
        await _email.SendAsync(user.Email, "Verify your StockWise account",
            $"Welcome to StockWise! Verify your email with this token: {user.EmailVerificationTokenHash}", ct);

        await _audit.WriteAsync("Auth.Registered", "User", user.Id, new { org.Id }, ct);
        await tx.CommitAsync(ct);

        return await IssueTokensAsync(user, org.Id, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, string ip, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Uniform failure response; never reveal whether the email exists.
        if (user is null || !user.IsActive)
            throw AppException.Unauthorized("Invalid email or password.");

        if (user.LockoutEndUtc is { } lockEnd && lockEnd > DateTime.UtcNow)
            throw new AppException("Account temporarily locked due to repeated failed sign-in attempts. Try again later.",
                429, "account_locked");

        if (!_hasher.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                user.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("Auth.LoginFailed", "User", user.Id, null, ct);
            throw AppException.Unauthorized("Invalid email or password.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;

        var membership = await _db.TenantMemberships.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active)
            .Join(_db.Organizations.Where(o => o.Status == OrganizationStatus.Active),
                m => m.OrganizationId, o => o.Id, (m, o) => new { m.OrganizationId, o.Name })
            .FirstOrDefaultAsync(ct);

        if (membership is null)
            throw AppException.Forbidden("Your account is not a member of any active organization.");

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Auth.LoggedIn", "User", user.Id, null, ct);
        return await IssueTokensAsync(user, membership.OrganizationId, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshTokenRaw, string ip, CancellationToken ct)
    {
        var hash = _tokens.HashToken(refreshTokenRaw);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive)
            throw AppException.Unauthorized("Invalid or expired session. Please sign in again.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct)
            ?? throw AppException.Unauthorized("Invalid or expired session. Please sign in again.");

        var membership = await _db.TenantMemberships.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active)
            .Join(_db.Organizations.Where(o => o.Status == OrganizationStatus.Active),
                m => m.OrganizationId, o => o.Id, (m, o) => new { m.OrganizationId, o.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw AppException.Forbidden("Your account is not a member of any active organization.");

        // Rotate: revoke the used token and link its replacement (detects token replay).
        stored.RevokedAtUtc = DateTime.UtcNow;
        return await IssueTokensAsync(user, membership.OrganizationId, ct, ip, stored.Id);
    }

    public async Task LogoutAsync(string? refreshTokenRaw, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(refreshTokenRaw))
        {
            var hash = _tokens.HashToken(refreshTokenRaw);
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored != null) stored.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        if (_currentUser.UserId is { } uid)
            await _audit.WriteAsync("Auth.LoggedOut", "User", uid, null, ct);
    }

    public async Task SwitchOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        await _permissions.EnsureMemberAsync(uid, organizationId, ct);
        // Token re-issuance happens in the controller using the validated membership.
    }

    public async Task<AuthResponse> SwitchOrganizationAsync(Guid organizationId, string ip, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        await _permissions.EnsureMemberAsync(uid, organizationId, ct);
        var user = await _db.Users.FirstAsync(u => u.Id == uid, ct);
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == organizationId, ct);
        return await IssueTokensAsync(user, org.Id, ct, ip);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);
        if (user != null)
        {
            var raw = _tokens.NewOpaqueToken();
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = _tokens.HashToken(raw),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            });
            await _db.SaveChangesAsync(ct);
            await _email.SendAsync(user.Email, "Reset your StockWise password",
                $"Use this token to reset your password (valid 1 hour): {raw}", ct);
        }
        // Always the same response shape: do not reveal account existence.
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var hash = _tokens.HashToken(req.Token);
        var token = await _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.IsUsed || token.ExpiresAtUtc < DateTime.UtcNow)
            throw AppException.BadRequest("This password reset link is invalid or has expired.");

        var user = await _db.Users.FirstAsync(u => u.Id == token.UserId, ct);
        user.PasswordHash = _hasher.Hash(req.Password);
        token.UsedAtUtc = DateTime.UtcNow;

        // Revoke every active session after a password reset.
        var sessions = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var s in sessions) s.RevokedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Auth.PasswordReset", "User", user.Id, null, ct);
    }

    public async Task VerifyEmailAsync(VerifyEmailRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailVerificationTokenHash == req.Token, ct)
            ?? throw AppException.BadRequest("This verification link is invalid.");
        user.EmailVerified = true;
        user.EmailVerificationTokenHash = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var user = await _db.Users.FirstAsync(u => u.Id == uid, ct);
        if (!_hasher.Verify(req.CurrentPassword, user.PasswordHash))
            throw AppException.Validation("The current password is incorrect.",
                new Dictionary<string, string[]> { ["currentPassword"] = ["Incorrect password."] });
        user.PasswordHash = _hasher.Hash(req.NewPassword);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Auth.PasswordChanged", "User", uid, null, ct);
    }

    public async Task<UserDto> UpdateProfileAsync(UpdateProfileRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var user = await _db.Users.FirstAsync(u => u.Id == uid, ct);
        user.FullName = req.FullName.Trim();
        await _db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Email, user.FullName, user.EmailVerified);
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == uid, ct);
        var tenantId = _currentUser.TenantId!.Value;

        // Roles are tenant-filtered; resolve names separately so memberships in ALL
        // organizations are listed (the active-org filter must not drop other orgs).
        var memberships = await (from m in _db.TenantMemberships.AsNoTracking()
                                 join o in _db.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
                                 where m.UserId == uid && m.Status == MembershipStatus.Active
                                 select new { o.Id, o.Name, m.RoleId }).ToListAsync(ct);
        var roleIds = memberships.Select(x => x.RoleId).Distinct().ToList();
        var roleNames = await _db.Roles.AsNoTracking().IgnoreQueryFilters()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var orgs = memberships
            .Select(x => new OrganizationSummaryDto(x.Id, x.Name, roleNames.GetValueOrDefault(x.RoleId, "Member")))
            .ToList();

        var perms = await _permissions.GetPermissionsAsync(uid, tenantId, ct);
        var active = orgs.FirstOrDefault(o => o.Id == tenantId) ?? orgs.FirstOrDefault()
            ?? throw AppException.Forbidden("You are not a member of any organization.");

        return new MeResponse(
            new UserDto(user.Id, user.Email, user.FullName, user.EmailVerified),
            active.Id, active.Name, orgs, [.. perms]);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, Guid tenantId, CancellationToken ct = default, string ip = "", Guid? replacedTokenId = null)
    {
        // Login/refresh/switch flows run without a tenant claim; scope the permission lookup to the target org.
        _tenantContext.SetTenant(tenantId);
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);
        var (access, expires) = _tokens.CreateAccessToken(user, tenantId, org.Name);

        var refreshRaw = _tokens.NewOpaqueToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokens.HashToken(refreshRaw),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedByIp = string.IsNullOrEmpty(ip) ? null : ip,
            ReplacedByTokenId = replacedTokenId
        };
        _db.RefreshTokens.Add(refresh);
        await _db.SaveChangesAsync(ct);

        var perms = await _permissions.GetPermissionsAsync(user.Id, tenantId, ct);
        return new AuthResponse(access, expires,
            new UserDto(user.Id, user.Email, user.FullName, user.EmailVerified),
            tenantId, org.Name, [.. perms])
        {
            RefreshToken = refreshRaw
        };
    }
}
