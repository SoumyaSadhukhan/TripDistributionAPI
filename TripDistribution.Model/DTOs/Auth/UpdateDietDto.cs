using TripDistribution.Model.Enums;

namespace TripDistribution.Model.DTOs.Auth;

public class UpdateDietDto
{
    public DietType DietType { get; set; }
    public string? DietName { get; set; }
}

public class ResetPasswordDto
{
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string OtpCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? AvatarColor { get; set; }
    public bool? IsBiometricEnabled { get; set; }
}
