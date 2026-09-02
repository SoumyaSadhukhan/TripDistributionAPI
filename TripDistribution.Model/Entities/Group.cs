namespace TripDistribution.Model.Entities;

public class Group
{
    public string GroupId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
