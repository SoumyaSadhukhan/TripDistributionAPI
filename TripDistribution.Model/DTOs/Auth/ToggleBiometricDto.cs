using System.ComponentModel.DataAnnotations;
namespace TripDistribution.Model.DTOs.Auth
{
    public class ToggleBiometricDto
    {
        [Required]
        public string UserId { get; set; } = string.Empty;
        [Required]
        public bool Enabled { get; set; }
    }
}
