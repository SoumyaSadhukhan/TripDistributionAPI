using System.ComponentModel.DataAnnotations;

namespace TripDistribution.Model.DTOs.Trips;

public class CreateTripDto
{
    public string? TripId { get; set; }

    [Required(ErrorMessage = "Trip name is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Trip name must be between 1 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Trip description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [StringLength(10, ErrorMessage = "Currency code cannot exceed 10 characters.")]
    public string CurrencyCode { get; set; } = "INR";
}

public class UpdateTripDto
{
    [Required(ErrorMessage = "Trip name is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Trip name must be between 1 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Trip description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [StringLength(10, ErrorMessage = "Currency code cannot exceed 10 characters.")]
    public string CurrencyCode { get; set; } = "INR";
}

public class TripSummaryDto
{
    public string TripId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public int MemberCount { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal MyPaidAmount { get; set; }
    public decimal MyOwedAmount { get; set; }
    public decimal MyBalance { get; set; }
    public bool IsOwner { get; set; }
    public string Role { get; set; } = "MEMBER";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
