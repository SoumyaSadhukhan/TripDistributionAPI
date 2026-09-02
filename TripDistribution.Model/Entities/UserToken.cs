namespace TripDistribution.Model.Entities;

public class UserToken
{
    public string TokenId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string AuthToken { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}
