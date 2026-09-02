namespace TripDistribution.Model.Entities;

public class TripCollaborator
{
    public string CollabId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? PersonId { get; set; }
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = "MEMBER";
    public string Status { get; set; } = "ACCEPTED";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
