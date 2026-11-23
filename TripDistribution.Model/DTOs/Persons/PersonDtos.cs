using TripDistribution.Model.Enums;

namespace TripDistribution.Model.DTOs.Persons;

public class CreatePersonDto
{
    public string TripId { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public string? UserId { get; set; }
    public string? PhoneNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DietType DietType { get; set; } = DietType.Vegetarian;
    public string? DietName { get; set; }
}

public class UpdatePersonDto
{
    public string? GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DietType DietType { get; set; }
    public string? DietName { get; set; }
}

public class PersonDto
{
    public string PersonId { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public string TripId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? PhoneNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DietType DietType { get; set; }
    public string DietName { get; set; } = "Vegetarian";
    public decimal PaidAmount { get; set; }
    public decimal OwedAmount { get; set; }
    public decimal Balance { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
