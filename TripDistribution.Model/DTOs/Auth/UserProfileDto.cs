using TripDistribution.Model.Enums;

namespace TripDistribution.Model.DTOs.Auth;

public class UserProfileDto
{
    public string UserId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public DietType DietType { get; set; }
    public string DietName { get; set; } = "Vegetarian";
    public string AvatarColor { get; set; } = "#4F46E5";
    public bool IsBiometricEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
