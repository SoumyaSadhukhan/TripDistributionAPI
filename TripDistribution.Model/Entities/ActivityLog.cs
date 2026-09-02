namespace TripDistribution.Model.Entities;

public class ActivityLog
{
    public string LogId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? TripId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
