namespace TripDistribution.Model.Entities;

public class ExpenseSplit
{
    public string SplitId { get; set; } = string.Empty;
    public string ExpenseId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string PersonId { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
