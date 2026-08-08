using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MangaHub.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MangaHub.Infrastructure.Security;

public sealed class JwtSessionTokenService(IOptions<MangaHubOptions> options) : ISessionTokenService
{
    public string CreateToken(Guid userId, string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, username),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("mangahub_iat_ticks", DateTimeOffset.UtcNow.UtcTicks.ToString(), ClaimValueTypes.Integer64)
            ],
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(options.Value.JwtExpiresMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid? ReadUserId(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(options.Value.JwtSecret);

        try
        {
            Console.WriteLine($"Token received length: {token.Length}");
            Console.WriteLine($"JWT secret length: {options.Value.JwtSecret.Length}");

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out var validatedToken);

            Console.WriteLine($"Validated token type: {validatedToken.GetType().Name}");

            foreach (var claim in principal.Claims)
            {
                Console.WriteLine($"Claim: {claim.Type} = {claim.Value}");
            }

            var sub =
                principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;

            Console.WriteLine($"Resolved sub: {sub}");

            var parsed = Guid.TryParse(sub, out var userId);
            Console.WriteLine($"Guid parsed: {parsed}, userId: {userId}");

            return parsed ? userId : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ReadUserId failed: {ex.GetType().Name}");
            Console.WriteLine(ex.Message);
            return null;
        }
    }

    public DateTimeOffset? ReadIssuedAt(string token)
    {
        try
        {
            var issuedAt = new JwtSecurityTokenHandler().ReadJwtToken(token)
                .Claims
                .FirstOrDefault(claim => claim.Type == "mangahub_iat_ticks")?.Value;
            return long.TryParse(issuedAt, out var ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
        catch
        {
            return null;
        }
    }
}
