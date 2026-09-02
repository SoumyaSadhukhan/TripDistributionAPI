namespace TripDistribution.Model.DTOs.Expenses;

public class ExpenseSplitItemDto
{
    public string PersonId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class CreateExpenseDto
{
    public string TripId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public int CategoryIndex { get; set; } = 0;
    public string CategoryName { get; set; } = "General";
    public string PaidByPersonId { get; set; } = string.Empty;
    public string SplitType { get; set; } = "EQUAL"; // EQUAL, EXACT, DIETARY
    public DateTime? ExpenseDate { get; set; }
    public List<ExpenseSplitItemDto> Splits { get; set; } = [];
}

public class UpdateExpenseDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public int CategoryIndex { get; set; } = 0;
    public string CategoryName { get; set; } = "General";
    public string PaidByPersonId { get; set; } = string.Empty;
    public string SplitType { get; set; } = "EQUAL";
    public DateTime? ExpenseDate { get; set; }
    public List<ExpenseSplitItemDto> Splits { get; set; } = [];
}

public class ExpenseDetailDto
{
    public string ExpenseId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public int CategoryIndex { get; set; }
    public string CategoryName { get; set; } = "General";
    public string PaidByPersonId { get; set; } = string.Empty;
    public string PaidByName { get; set; } = string.Empty;
    public string SplitType { get; set; } = "EQUAL";
    public DateTime ExpenseDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ExpenseSplitItemDto> Splits { get; set; } = [];
}

public class DietarySplitCalculateDto
{
    public decimal TotalAmount { get; set; }
    public int CategoryIndex { get; set; } // 0: Food/General, 1: NonVeg, 2: Alcohol, etc.
    public List<string> PersonIds { get; set; } = [];
}
