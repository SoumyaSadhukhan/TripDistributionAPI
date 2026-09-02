namespace TripDistribution.Model.Entities;

public class Expense
{
    public string ExpenseId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
    public string CurrencyCode { get; set; } = "INR";
    public int CategoryIndex { get; set; } = 0;
    public string CategoryName { get; set; } = "General";
    public string PaidByPersonId { get; set; } = string.Empty;
    public string SplitType { get; set; } = "EQUAL";
    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
