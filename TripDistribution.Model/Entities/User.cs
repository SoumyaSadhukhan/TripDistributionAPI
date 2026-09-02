using TripDistribution.Model.Enums;

namespace TripDistribution.Model.Entities;

public class User
{
    public string UserId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? PasswordHash { get; set; }
    public string? AuthToken { get; set; }
    public DateTime? TokenExpiry { get; set; }
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string DietName { get; set; } = "Vegetarian";
    public string AvatarColor { get; set; } = "#4F46E5";
    public bool IsBiometricEnabled { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
