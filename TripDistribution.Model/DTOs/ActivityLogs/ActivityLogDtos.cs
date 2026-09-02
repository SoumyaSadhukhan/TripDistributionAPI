namespace TripDistribution.Model.DTOs.ActivityLogs;

public class ActivityLogDto
{
    public string LogId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? TripId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateActivityLogDto
{
    public string? TripId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? Details { get; set; }
}
