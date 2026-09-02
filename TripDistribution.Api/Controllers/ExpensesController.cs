using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Expenses;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly IExpenseDataService _expenseDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly INotificationDataService _notificationDataService;
    private readonly IPersonDataService _personDataService;
    private readonly ILogger<ExpensesController> _logger;

    public ExpensesController(
        IExpenseDataService expenseDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        INotificationDataService notificationDataService,
        IPersonDataService personDataService,
        ILogger<ExpensesController> logger)
    {
        _expenseDataService = expenseDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _notificationDataService = notificationDataService;
        _personDataService = personDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetExpensesByTrip(string tripId)
    {
        try
        {
            var expenses = await _expenseDataService.GetExpensesByTripAsync(tripId);
            return Ok(ApiResponse<IEnumerable<ExpenseDetailDto>>.Ok(expenses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetExpensesByTrip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch expenses."));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetExpenseById(string id)
    {
        try
        {
            var expense = await _expenseDataService.GetExpenseByIdAsync(id);
            if (expense == null)
                return NotFound(ApiResponse.Fail("Expense not found."));

            return Ok(ApiResponse<ExpenseDetailDto>.Ok(expense));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetExpenseById: {ExpenseId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch expense details."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateExpense([FromBody] CreateExpenseDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.TripId) || string.IsNullOrWhiteSpace(dto.Description) || dto.Amount <= 0)
                return BadRequest(ApiResponse.Fail("TripId, Description, and valid Amount (> 0) are required."));

            if (string.IsNullOrWhiteSpace(dto.PaidByPersonId))
                return BadRequest(ApiResponse.Fail("PaidByPersonId is required."));

            var expense = await _expenseDataService.CreateExpenseAsync(dto);

            var userId = _currentUserContext.UserId;
            await _activityLogDataService.LogActivityAsync(userId, dto.TripId, "ADD_EXPENSE", $"Added expense '{dto.Description}' of {dto.CurrencyCode} {dto.Amount}");
            _logger.LogInformation("Expense {ExpenseId} created for trip {TripId}", expense.ExpenseId, dto.TripId);

            // Notify other participants
            var persons = await _personDataService.GetPersonsByTripAsync(dto.TripId);
            foreach (var p in persons)
            {
                if (!string.IsNullOrWhiteSpace(p.UserId) && p.UserId != userId)
                {
                    await _notificationDataService.CreateNotificationAsync(new Notification
                    {
                        UserId = p.UserId,
                        TripId = dto.TripId,
                        Title = "New Expense Added",
                        Message = $"{expense.PaidByName} added '{dto.Description}' ({dto.CurrencyCode} {dto.Amount:N2})",
                        Type = "EXPENSE_ADDED",
                        Amount = dto.Amount
                    });
                }
            }

            return CreatedAtAction(nameof(GetExpenseById), new { id = expense.ExpenseId }, ApiResponse<ExpenseDetailDto>.Ok(expense, "Expense added successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CreateExpense for trip: {TripId}", dto.TripId);
            return StatusCode(500, ApiResponse.Fail("Failed to add expense."));
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateExpense(string id, [FromBody] UpdateExpenseDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Description) || dto.Amount <= 0)
                return BadRequest(ApiResponse.Fail("Description and valid Amount (> 0) are required."));

            var updated = await _expenseDataService.UpdateExpenseAsync(id, dto);
            if (!updated)
                return NotFound(ApiResponse.Fail("Expense not found."));

            var expense = await _expenseDataService.GetExpenseByIdAsync(id);
            await _activityLogDataService.LogActivityAsync(_currentUserContext.UserId, expense?.TripId, "UPDATE_EXPENSE", $"Updated expense '{dto.Description}'");
            _logger.LogInformation("Expense {ExpenseId} updated", id);

            return Ok(ApiResponse<ExpenseDetailDto>.Ok(expense!, "Expense updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateExpense: {ExpenseId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update expense."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteExpense(string id)
    {
        try
        {
            var existing = await _expenseDataService.GetExpenseByIdAsync(id);
            if (existing == null)
                return NotFound(ApiResponse.Fail("Expense not found."));

            var deleted = await _expenseDataService.DeleteExpenseAsync(id);
            if (!deleted)
                return NotFound(ApiResponse.Fail("Expense could not be deleted."));

            await _activityLogDataService.LogActivityAsync(_currentUserContext.UserId, existing.TripId, "DELETE_EXPENSE", $"Deleted expense '{existing.Description}'");
            _logger.LogInformation("Expense {ExpenseId} deleted", id);

            return Ok(ApiResponse.Ok("Expense deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeleteExpense: {ExpenseId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to delete expense."));
        }
    }

    [HttpPost("calculate-dietary-split/{tripId}")]
    public async Task<IActionResult> CalculateDietarySplit(string tripId, [FromBody] DietarySplitCalculateDto dto)
    {
        try
        {
            var splits = await _expenseDataService.CalculateDietarySplitsAsync(tripId, dto.TotalAmount, dto.CategoryIndex, dto.PersonIds);
            return Ok(ApiResponse<List<ExpenseSplitItemDto>>.Ok(splits));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CalculateDietarySplit for trip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to calculate dietary split."));
        }
    }
}
