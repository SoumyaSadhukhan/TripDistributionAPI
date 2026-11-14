using System.ComponentModel.DataAnnotations;
using TripDistribution.Model.Enums;

namespace TripDistribution.Model.DTOs.Friends;

public class FriendInviteDto
{
    public string FriendPhone { get; set; } = string.Empty;
    public string FriendName { get; set; } = string.Empty;
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string? DietName { get; set; }
}

public class CheckContactsDto
{
    [MaxLength(100, ErrorMessage = "A maximum of 100 contacts can be verified per request.")]
    public List<string> Contacts { get; set; } = [];
}

public class ContactMatchResultDto
{
    public string UserId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DietType DietType { get; set; }
    public string DietName { get; set; } = "Vegetarian";
    public bool IsRegistered { get; set; } = true;
}

public class FriendDto
{
    public string ConnectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? FriendUserId { get; set; }
    public string FriendPhone { get; set; } = string.Empty;
    public string FriendName { get; set; } = string.Empty;
    public DietType DietType { get; set; }
    public string DietName { get; set; } = "Vegetarian";
    public string Status { get; set; } = "ACCEPTED";
    public string? RequesterId { get; set; }
    public string? ReceiverId { get; set; }
    public bool IsIncoming { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateFriendStatusDto
{
    public string Status { get; set; } = "ACCEPTED"; // ACCEPTED, REJECTED
}
