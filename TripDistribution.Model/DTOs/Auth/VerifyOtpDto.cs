namespace TripDistribution.Model.DTOs.Auth;

public class VerifyOtpDto
{
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string OtpCode { get; set; } = string.Empty;
    public string Purpose { get; set; } = "REGISTER";
}
