using System.ComponentModel.DataAnnotations;
namespace TripDistribution.Model.DTOs.Auth
{
    public class VerifyTokenDto
    {
        [Required]
        public string Token { get; set; } = string.Empty;
        public string? Phone { get; set; }
    }
}
