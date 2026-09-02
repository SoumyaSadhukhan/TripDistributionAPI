namespace TripDistribution.Model.DTOs.Auth;

public class BiometricLoginDto
{
    public string UserId { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}
