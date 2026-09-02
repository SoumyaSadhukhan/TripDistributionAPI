using TripDistribution.Model.Enums;

namespace TripDistribution.Model.DTOs.Auth;

public class RegisterRequestDto
{
    public string Phone { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string? DietName { get; set; }
    public string? AvatarColor { get; set; }
    public string? DeviceInfo { get; set; }
}
