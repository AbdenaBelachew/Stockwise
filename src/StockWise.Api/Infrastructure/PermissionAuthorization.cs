using Microsoft.AspNetCore.Authorization;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Exceptions;

namespace StockWise.Api.Infrastructure;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

/// <summary>Authorizes a request when the user holds the required permission in the active tenant.</summary>
public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionChecker _checker;
    private readonly ICurrentUser _currentUser;

    public PermissionHandler(IPermissionChecker checker, ICurrentUser currentUser)
    {
        _checker = checker;
        _currentUser = currentUser;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (_currentUser.UserId is not { } uid || _currentUser.TenantId is not { } tid)
            return;
        try
        {
            await _checker.EnsurePermissionAsync(uid, tid, requirement.Permission);
            context.Succeed(requirement);
        }
        catch (AppException)
        {
            // failure = requirement not met; endpoint returns 403
        }
    }
}

/// <summary>Creates "perm:Products.Read"-style policies on demand.</summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith("perm:", StringComparison.OrdinalIgnoreCase))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[5..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>Attribute shortcut: [AuthorizePermission(Permissions.Products.Create)].</summary>
public sealed class AuthorizePermissionAttribute : AuthorizeAttribute
{
    public AuthorizePermissionAttribute(string permission) : base($"perm:{permission}") { }
}
