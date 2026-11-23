using TripDistribution.Model.Enums;

namespace TripDistribution.Model.Entities;

public class Person
{
    public string PersonId { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public string TripId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? PhoneNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string DietName { get; set; } = "Vegetarian";
    public decimal PaidAmount { get; set; } = 0.00m;
    public decimal OwedAmount { get; set; } = 0.00m;
    public decimal Balance { get; set; } = 0.00m;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
