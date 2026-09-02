namespace TripDistribution.Model.DTOs.Auth;

public class VerifyOtpDto
{
    public string Phone { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public string Purpose { get; set; } = "REGISTER";
}
