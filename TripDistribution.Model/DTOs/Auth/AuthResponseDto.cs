using TripDistribution.Model.Enums;

namespace TripDistribution.Model.DTOs.Auth;

public class AuthResponseDto
{
    public string UserId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Token { get; set; } = string.Empty;
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string DietName { get; set; } = "Vegetarian";
    public string AvatarColor { get; set; } = "#4F46E5";
    public bool IsBiometricEnabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
