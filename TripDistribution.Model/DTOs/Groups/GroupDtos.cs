namespace TripDistribution.Model.DTOs.Groups;

public class CreateGroupDto
{
    public string TripId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class UpdateGroupDto
{
    public string Name { get; set; } = string.Empty;
}

public class GroupDto
{
    public string GroupId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
