namespace TripDistribution.Model.DTOs.Auth;

public class SendOtpDto
{
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Purpose { get; set; } = "REGISTER"; // REGISTER, FORGOT_PASSWORD, LOGIN
}
