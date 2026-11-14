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
    private const string DefaultSecretKey = "SplitBillSuperSecretKey_ForTripDistribution_ProductionReady_2026_Key_MustBeLongEnough!";
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public JwtTokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private string GetSecretKey()
    {
        var key = _configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(key) || key == DefaultSecretKey)
        {
            if (_environment.IsProduction())
            {
                throw new InvalidOperationException("Production JWT SecretKey is missing or using default fallback! Configure a strong secret key in environment variables.");
            }
            return DefaultSecretKey;
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("JWT SecretKey must be at least 32 characters (256 bits) long.");
        }

        return key;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        var secretKey = GetSecretKey();
        var issuer = _configuration["JwtSettings:Issuer"] ?? "TripDistribution.Api";
        var audience = _configuration["JwtSettings:Audience"] ?? "TripDistribution.Client";
        var expiryMinutes = int.TryParse(_configuration["JwtSettings:ExpiryInMinutes"], out var mins) ? mins : 1440; // 24 hours

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        // Every token contains a globally unique nonce (JTI) and timestamp (IAT)
        var jti = Guid.NewGuid().ToString();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat, iat, ClaimValueTypes.Integer64),
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
        var secretKey = GetSecretKey();
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
