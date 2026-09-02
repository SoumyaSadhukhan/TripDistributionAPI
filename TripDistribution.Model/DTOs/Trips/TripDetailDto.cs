using TripDistribution.Model.DTOs.Expenses;
using TripDistribution.Model.DTOs.Groups;
using TripDistribution.Model.DTOs.Persons;
using TripDistribution.Model.DTOs.Settlements;

namespace TripDistribution.Model.DTOs.Trips;

public class TripDetailDto
{
    public string TripId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public List<GroupDto> Groups { get; set; } = [];
    public List<PersonDto> Persons { get; set; } = [];
    public List<ExpenseDetailDto> Expenses { get; set; } = [];
    public List<SettlementDto> Settlements { get; set; } = [];
}
