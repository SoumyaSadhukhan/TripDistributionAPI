using System.Security.Claims;
using TripDistribution.Model.Constants;
using TripDistribution.Model.Enums;

namespace TripDistribution.Api.Services;

public interface ICurrentUserContext
{
    string? UserId { get; }
    string? PhoneNumber { get; }
    string? FullName { get; }
    DietType DietType { get; }
    bool IsAuthenticated { get; }
}

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId =>
        User?.FindFirst(AuthConstants.UserIdClaim)?.Value 
        ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public string? PhoneNumber =>
        User?.FindFirst(AuthConstants.PhoneClaim)?.Value 
        ?? User?.FindFirst(ClaimTypes.MobilePhone)?.Value;

    public string? FullName =>
        User?.FindFirst(AuthConstants.FullNameClaim)?.Value 
        ?? User?.FindFirst(ClaimTypes.Name)?.Value;

    public DietType DietType
    {
        get
        {
            var raw = User?.FindFirst("dietType")?.Value;
            return int.TryParse(raw, out var val) ? (DietType)val : DietType.Vegetarian;
        }
    }

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
