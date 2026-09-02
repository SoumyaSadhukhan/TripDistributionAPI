namespace TripDistribution.Model.Entities;

public class Notification
{
    public string NotificationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public string? TripName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "EXPENSE_ADDED";
    public decimal Amount { get; set; } = 0.00m;
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
