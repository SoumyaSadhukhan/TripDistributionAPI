namespace TripDistribution.Model.DTOs.Trips;

public class CreateTripDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CurrencyCode { get; set; } = "INR";
}

public class UpdateTripDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
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
