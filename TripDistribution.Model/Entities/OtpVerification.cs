namespace TripDistribution.Model.Entities;

public class OtpVerification
{
    public string OtpId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string OtpCode { get; set; } = string.Empty;
    public string? OtpHash { get; set; }
    public string Purpose { get; set; } = "REGISTER";
    public bool IsVerified { get; set; } = false;
    public int FailedAttempts { get; set; } = 0;
    public int MaxAttempts { get; set; } = 5;
    public DateTime? LockoutUntil { get; set; }
    public string? IpAddress { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
