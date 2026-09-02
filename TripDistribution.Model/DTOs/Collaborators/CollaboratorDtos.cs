namespace TripDistribution.Model.DTOs.Collaborators;

public class InviteCollaboratorDto
{
    public string TripId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? PersonId { get; set; }
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = "MEMBER"; // OWNER, MEMBER, VIEWER
}

public class CollaboratorDto
{
    public string CollabId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? FullName { get; set; }
    public string Role { get; set; } = "MEMBER";
    public string Status { get; set; } = "ACCEPTED";
    public DateTime JoinedAt { get; set; }
}

public class UpdateCollaboratorRoleDto
{
    public string Role { get; set; } = "MEMBER";
}
