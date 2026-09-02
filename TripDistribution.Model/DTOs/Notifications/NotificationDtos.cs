namespace TripDistribution.Model.DTOs.Notifications;

public class NotificationDto
{
    public string NotificationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public string? TripName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "EXPENSE_ADDED";
    public decimal Amount { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MarkNotificationReadDto
{
    public string? NotificationId { get; set; }
    public bool MarkAllAsRead { get; set; } = false;
}
