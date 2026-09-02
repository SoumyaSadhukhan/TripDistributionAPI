namespace TripDistribution.Model.DTOs.Settlements;

public class ProposeSettlementDto
{
    public string TripId { get; set; } = string.Empty;
    public string FromPersonId { get; set; } = string.Empty;
    public string ToPersonId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class UpdateSettlementStatusDto
{
    public string Status { get; set; } = "ACCEPTED"; // ACCEPTED, DECLINED
}

public class SettlementDto
{
    public string SettlementId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string FromPersonId { get; set; } = string.Empty;
    public string FromPersonName { get; set; } = string.Empty;
    public string ToPersonId { get; set; } = string.Empty;
    public string ToPersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsCompleted { get; set; }
    public string Status { get; set; } = "ACCEPTED";
    public string? CreatedByUserId { get; set; }
    public DateTime SettledAt { get; set; }
}
