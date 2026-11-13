namespace TripDistribution.Model.DTOs.Auth;

public class LoginRequestDto
{
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? UserId { get; set; }
    public string Password { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}
