using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using BellNotification.Application.Dtos;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BellNotification.API.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class StreamTokenController : ControllerBase
{
    private readonly ILogger<StreamTokenController> _logger;

    public StreamTokenController(ILogger<StreamTokenController> logger)
    {
        _logger = logger;
    }

    [HttpPost("stream-token")]
    public ActionResult<StreamTokenResponse> GetStreamToken()
    {
        var userId = User.FindFirstValue("sub")
            ?? User.FindFirstValue("user_id")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        var tenantId = User.FindFirstValue("tenant_id");

        var signingKey = Environment.GetEnvironmentVariable("JWT_STREAM_TOKEN_SIGNING_KEY")
            ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
            ?? throw new InvalidOperationException("Stream token signing key not configured");

        var expiresInMinutesStr = Environment.GetEnvironmentVariable("JWT_STREAM_TOKEN_EXPIRATION_MINUTES");
        var expiresInMinutes = int.TryParse(expiresInMinutesStr, out var minutes) ? minutes : 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresInMinutes);

        var claims = new List<Claim>
        {
            new("sub", userId),
            new("user_id", userId),
            new("purpose", "sse")
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            claims.Add(new Claim("tenant_id", tenantId));
        }

        var key = Encoding.UTF8.GetBytes(signingKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new StreamTokenResponse
        {
            StreamToken = tokenString,
            ExpiresAtUtc = expiresAt
        });
    }
}
