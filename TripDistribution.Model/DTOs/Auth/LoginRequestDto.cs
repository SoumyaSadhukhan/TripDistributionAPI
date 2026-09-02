namespace TripDistribution.Model.DTOs.Auth;

public class LoginRequestDto
{
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}
