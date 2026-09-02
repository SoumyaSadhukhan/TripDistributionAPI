using TripDistribution.Model.Enums;

namespace TripDistribution.Model.Entities;

public class FriendConnection
{
    public string ConnectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? FriendUserId { get; set; }
    public string FriendPhone { get; set; } = string.Empty;
    public string FriendName { get; set; } = string.Empty;
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string DietName { get; set; } = "Vegetarian";
    public string Status { get; set; } = "ACCEPTED";
    public string? RequesterId { get; set; }
    public string? ReceiverId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
