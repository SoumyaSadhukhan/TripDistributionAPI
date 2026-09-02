using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TripDistribution.Model.Constants;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
    ClaimsPrincipal? ValidateToken(string token);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        var secretKey = _configuration["JwtSettings:SecretKey"];
        var issuer = _configuration["JwtSettings:Issuer"] ?? "TripDistribution.Api";
        var audience = _configuration["JwtSettings:Audience"] ?? "TripDistribution.Client";
        var expiryMinutes = int.TryParse(_configuration["JwtSettings:ExpiryInMinutes"], out var mins) ? mins : 43200; // 30 days

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(AuthConstants.UserIdClaim, user.UserId),
            new(AuthConstants.PhoneClaim, user.PhoneNumber),
            new(ClaimTypes.MobilePhone, user.PhoneNumber),
            new(AuthConstants.FullNameClaim, user.FullName ?? string.Empty),
            new(ClaimTypes.Name, user.FullName ?? user.PhoneNumber),
            new("dietType", ((int)user.DietType).ToString()),
            new("dietName", user.DietName)
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenString = tokenHandler.WriteToken(tokenDescriptor);

        return (tokenString, expiresAt);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var secretKey = _configuration["JwtSettings:SecretKey"] 
            ?? "SplitBillSuperSecretKey_ForTripDistribution_ProductionReady_2026_Key_MustBeLongEnough!";
        var issuer = _configuration["JwtSettings:Issuer"] ?? "TripDistribution.Api";
        var audience = _configuration["JwtSettings:Audience"] ?? "TripDistribution.Client";

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
