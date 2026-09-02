namespace TripDistribution.Model.DTOs.Auth;

public class SendOtpDto
{
    public string Phone { get; set; } = string.Empty;
    public string Purpose { get; set; } = "REGISTER"; // REGISTER, FORGOT_PASSWORD, LOGIN
}
