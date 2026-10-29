using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StockWise.Application.Abstractions;
using StockWise.Domain.Entities;
using StockWise.Infrastructure.Persistence;

namespace StockWise.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IConfiguration config, ILogger<TokenService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private string Key
    {
        get
        {
            var key = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
                throw new InvalidOperationException(
                    "Jwt:Key is missing or too short (min 32 chars). Configure it via environment variable Jwt__Key. " +
                    "Never run production with the development default.");
            return key;
        }
    }

    public string Issuer => _config["Jwt:Issuer"] ?? "stockwise";
    public string Audience => _config["Jwt:Audience"] ?? "stockwise-web";
    public static TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(15);

    public (string token, DateTime expiresAtUtc) CreateAccessToken(User user, Guid tenantId, string orgName)
    {
        var expires = DateTime.UtcNow.Add(AccessTokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.FullName),
            new("tid", tenantId.ToString()),
            new("org", orgName)
        };

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string NewOpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Development email mode: writes emails to the email_messages table (visible in the app/DB)
/// and logs them. No real email is sent until a production provider is configured.
/// </summary>
public class DevEmailSender : IEmailSender
{
    private readonly StockWiseDbContext _db;
    private readonly ILogger<DevEmailSender> _logger;

    public DevEmailSender(StockWiseDbContext db, ILogger<DevEmailSender> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _db.EmailMessages.Add(new EmailMessage { To = to, Subject = subject, Body = body });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[DEV EMAIL] To={To} Subject={Subject}\n{Body}", to, subject, body);
    }
}
