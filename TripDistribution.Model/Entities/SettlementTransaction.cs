namespace TripDistribution.Model.Entities;

public class SettlementTransaction
{
    public string SettlementId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string FromPersonId { get; set; } = string.Empty;
    public string ToPersonId { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
    public bool IsCompleted { get; set; } = false;
    public string Status { get; set; } = "ACCEPTED";
    public string? CreatedByUserId { get; set; }
    public DateTime SettledAt { get; set; } = DateTime.UtcNow;
}
