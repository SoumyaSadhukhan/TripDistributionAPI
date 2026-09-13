using System.ComponentModel.DataAnnotations;
namespace TripDistribution.Model.DTOs.Friends
{
    public class SearchFriendsDto
    {
        [Required]
        public string Phone { get; set; } = string.Empty;
    }
}
