namespace TripDistribution.Model.DTOs.Notifications;

public class SendMessageDto
{
    public string ToUserId { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public string Title { get; set; } = "New Message";
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "CHAT_MESSAGE";
}
