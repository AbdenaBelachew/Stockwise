using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using StockWise.Api.Infrastructure;
using StockWise.Application.Features.Auth;
using StockWise.Domain.Exceptions;

namespace StockWise.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IHostEnvironment _env;

    public AuthController(AuthService auth, IHostEnvironment env)
    {
        _auth = auth;
        _env = env;
    }

    /// <summary>Registers a user and creates their first organization.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req, CancellationToken ct)
    {
        var res = await _auth.RegisterAsync(req, ct);
        SetRefreshCookie(res.RefreshToken!);
        return Ok(res);
    }

    /// <summary>Signs in with email and password.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var res = await _auth.LoginAsync(req, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", ct);
        SetRefreshCookie(res.RefreshToken!);
        return Ok(res);
    }

    /// <summary>Exchanges the HttpOnly refresh cookie for a new access token (rotates the refresh token).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[CookieNames.RefreshToken]
            ?? throw AppException.Unauthorized("No active session. Please sign in again.");
        var res = await _auth.RefreshAsync(raw, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", ct);
        SetRefreshCookie(res.RefreshToken!);
        return Ok(res);
    }

    /// <summary>Signs out: revokes the refresh token and clears the cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _auth.LogoutAsync(Request.Cookies[CookieNames.RefreshToken], ct);
        Response.Cookies.Delete(CookieNames.RefreshToken, RefreshCookieOptions());
        Response.Cookies.Delete(CookieNames.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
        Response.Cookies.Delete(CookieNames.RefreshToken);
        return NoContent();
    }

    /// <summary>Switches the active organization (for users with multiple memberships).</summary>
    [HttpPost("switch-organization")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> SwitchOrganization(SwitchOrganizationRequest req, CancellationToken ct)
    {
        var res = await _auth.SwitchOrganizationAsync(req.OrganizationId,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", ct);
        SetRefreshCookie(res.RefreshToken!);
        return Ok(res);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct) => Ok(await _auth.GetMeAsync(ct));

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
    {
        await _auth.ForgotPasswordAsync(req, ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(req, ct);
        return NoContent();
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest req, CancellationToken ct)
    {
        await _auth.VerifyEmailAsync(req, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(req, ct);
        return NoContent();
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileRequest req, CancellationToken ct) =>
        Ok(await _auth.UpdateProfileAsync(req, ct));

    private void SetRefreshCookie(string rawToken)
    {
        Response.Cookies.Append(CookieNames.RefreshToken, rawToken, RefreshCookieOptions());
    }

    private CookieOptions RefreshCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = !_env.IsDevelopment(), // dev runs over http; production requires HTTPS
        SameSite = SameSiteMode.Lax,
        Path = "/api/v1/auth",
        MaxAge = TimeSpan.FromDays(7)
    };
}
