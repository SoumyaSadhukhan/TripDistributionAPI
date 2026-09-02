namespace TripDistribution.Model.Entities;

public class OtpVerification
{
    public string OtpId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public string Purpose { get; set; } = "REGISTER";
    public bool IsVerified { get; set; } = false;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
