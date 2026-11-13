using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Expenses;
using TripDistribution.Model.DTOs.Persons;
using TripDistribution.Model.Entities;

namespace TripDistribution.Data.Services;

public interface IExpenseDataService
{
    Task<IEnumerable<ExpenseDetailDto>> GetExpensesByTripAsync(string tripId);
    Task<ExpenseDetailDto?> GetExpenseByIdAsync(string expenseId);
    Task<ExpenseDetailDto> CreateExpenseAsync(CreateExpenseDto dto);
    Task<bool> UpdateExpenseAsync(string expenseId, UpdateExpenseDto dto);
    Task<bool> DeleteExpenseAsync(string expenseId);
    Task<List<ExpenseSplitItemDto>> CalculateDietarySplitsAsync(string tripId, decimal totalAmount, int categoryIndex, List<string> selectedPersonIds);
}

public class ExpenseDataService : IExpenseDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPersonDataService _personDataService;
    private readonly ILogger<ExpenseDataService> _logger;

    public ExpenseDataService(
        IDbConnectionFactory connectionFactory, 
        IPersonDataService personDataService,
        ILogger<ExpenseDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _personDataService = personDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<ExpenseDetailDto>> GetExpensesByTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string expensesSql = @"
                SELECT e.expense_id AS ExpenseId, e.trip_id AS TripId, e.description AS Description,
                       e.amount AS Amount, e.currency_code AS CurrencyCode, e.category_index AS CategoryIndex,
                       e.category_name AS CategoryName, e.paid_by_person_id AS PaidByPersonId,
                       p.name AS PaidByName, e.split_type AS SplitType, e.expense_date AS ExpenseDate,
                       e.created_at AS CreatedAt
                FROM Expenses e
                LEFT JOIN Persons p ON e.paid_by_person_id = p.person_id
                WHERE e.trip_id = @TripId
                ORDER BY e.expense_date DESC";

            var expenses = (await conn.QueryAsync<ExpenseDetailDto>(expensesSql, new { TripId = tripId })).ToList();

            const string splitsSql = @"
                SELECT split_id AS SplitId, expense_id AS ExpenseId, person_id AS PersonId, amount AS Amount
                FROM ExpenseSplits WHERE trip_id = @TripId";
            var allSplits = (await conn.QueryAsync<dynamic>(splitsSql, new { TripId = tripId })).ToList();

            var splitsGrouped = allSplits
                .GroupBy(s => (string)s.ExpenseId)
                .ToDictionary(g => g.Key, g => g.Select(s => new ExpenseSplitItemDto
                {
                    PersonId = (string)s.PersonId,
                    Amount = (decimal)s.Amount
                }).ToList());

            foreach (var exp in expenses)
            {
                if (splitsGrouped.TryGetValue(exp.ExpenseId, out var sps))
                {
                    exp.Splits = sps;
                }
            }

            return expenses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expenses for trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task<ExpenseDetailDto?> GetExpenseByIdAsync(string expenseId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT e.expense_id AS ExpenseId, e.trip_id AS TripId, e.description AS Description,
                       e.amount AS Amount, e.currency_code AS CurrencyCode, e.category_index AS CategoryIndex,
                       e.category_name AS CategoryName, e.paid_by_person_id AS PaidByPersonId,
                       p.name AS PaidByName, e.split_type AS SplitType, e.expense_date AS ExpenseDate,
                       e.created_at AS CreatedAt
                FROM Expenses e
                LEFT JOIN Persons p ON e.paid_by_person_id = p.person_id
                WHERE e.expense_id = @ExpenseId";

            var expense = await conn.QuerySingleOrDefaultAsync<ExpenseDetailDto>(sql, new { ExpenseId = expenseId });
            if (expense == null) return null;

            const string splitsSql = @"
                SELECT person_id AS PersonId, amount AS Amount 
                FROM ExpenseSplits 
                WHERE expense_id = @ExpenseId";
            expense.Splits = (await conn.QueryAsync<ExpenseSplitItemDto>(splitsSql, new { ExpenseId = expenseId })).ToList();

            return expense;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expense by ID: {ExpenseId}", expenseId);
            throw;
        }
    }

    public async Task<ExpenseDetailDto> CreateExpenseAsync(CreateExpenseDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var expenseId = Guid.NewGuid().ToString();
            var expenseDate = dto.ExpenseDate ?? DateTime.UtcNow;

            var splits = dto.Splits;
            if (splits == null || splits.Count == 0)
            {
                var persons = (await _personDataService.GetPersonsByTripAsync(dto.TripId)).ToList();
                if (persons.Count > 0)
                {
                    var perPerson = Math.Round(dto.Amount / persons.Count, 2);
                    splits = persons.Select(p => new ExpenseSplitItemDto
                    {
                        PersonId = p.PersonId,
                        Amount = perPerson
                    }).ToList();
                }
                else
                {
                    splits = [];
                }
            }

            const string insertExpense = @"
                INSERT INTO Expenses (expense_id, trip_id, description, amount, currency_code, category_index, category_name, paid_by_person_id, split_type, expense_date, created_at)
                VALUES (@ExpenseId, @TripId, @Description, @Amount, @CurrencyCode, @CategoryIndex, @CategoryName, @PaidByPersonId, @SplitType, @ExpenseDate, GETDATE())";

            using var trans = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(insertExpense, new
                {
                    ExpenseId = expenseId,
                    dto.TripId,
                    dto.Description,
                    dto.Amount,
                    CurrencyCode = !string.IsNullOrWhiteSpace(dto.CurrencyCode) ? dto.CurrencyCode : "INR",
                    dto.CategoryIndex,
                    CategoryName = !string.IsNullOrWhiteSpace(dto.CategoryName) ? dto.CategoryName : "General",
                    dto.PaidByPersonId,
                    SplitType = !string.IsNullOrWhiteSpace(dto.SplitType) ? dto.SplitType : "EQUAL",
                    ExpenseDate = expenseDate
                }, transaction: trans);

                if (splits.Count > 0)
                {
                    const string insertSplit = @"
                        INSERT INTO ExpenseSplits (split_id, expense_id, trip_id, person_id, amount, created_at)
                        VALUES (@SplitId, @ExpenseId, @TripId, @PersonId, @Amount, GETDATE())";

                    var splitEntities = splits.Select(s => new
                    {
                        SplitId = Guid.NewGuid().ToString(),
                        ExpenseId = expenseId,
                        TripId = dto.TripId,
                        PersonId = s.PersonId,
                        Amount = s.Amount
                    });

                    await conn.ExecuteAsync(insertSplit, splitEntities, transaction: trans);
                }

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            _logger.LogInformation("Created expense {ExpenseId} for trip {TripId}, amount {Amount}", expenseId, dto.TripId, dto.Amount);

            await _personDataService.RecalculateTripBalancesAsync(dto.TripId);
            return (await GetExpenseByIdAsync(expenseId))!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating expense for trip: {TripId}", dto.TripId);
            throw;
        }
    }

    public async Task<bool> UpdateExpenseAsync(string expenseId, UpdateExpenseDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var existing = await GetExpenseByIdAsync(expenseId);
            if (existing == null) return false;

            const string updateSql = @"
                UPDATE Expenses 
                SET description = @Description, amount = @Amount, currency_code = @CurrencyCode,
                    category_index = @CategoryIndex, category_name = @CategoryName,
                    paid_by_person_id = @PaidByPersonId, split_type = @SplitType,
                    expense_date = COALESCE(@ExpenseDate, expense_date)
                WHERE expense_id = @ExpenseId";

            using var trans = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(updateSql, new
                {
                    ExpenseId = expenseId,
                    dto.Description,
                    dto.Amount,
                    CurrencyCode = !string.IsNullOrWhiteSpace(dto.CurrencyCode) ? dto.CurrencyCode : "INR",
                    dto.CategoryIndex,
                    CategoryName = !string.IsNullOrWhiteSpace(dto.CategoryName) ? dto.CategoryName : "General",
                    dto.PaidByPersonId,
                    SplitType = !string.IsNullOrWhiteSpace(dto.SplitType) ? dto.SplitType : "EQUAL",
                    dto.ExpenseDate
                }, transaction: trans);

                await conn.ExecuteAsync("DELETE FROM ExpenseSplits WHERE expense_id = @ExpenseId", new { ExpenseId = expenseId }, transaction: trans);

                if (dto.Splits != null && dto.Splits.Count > 0)
                {
                    const string insertSplit = @"
                        INSERT INTO ExpenseSplits (split_id, expense_id, trip_id, person_id, amount, created_at)
                        VALUES (@SplitId, @ExpenseId, @TripId, @PersonId, @Amount, GETDATE())";

                    var splitEntities = dto.Splits.Select(s => new
                    {
                        SplitId = Guid.NewGuid().ToString(),
                        ExpenseId = expenseId,
                        TripId = existing.TripId,
                        PersonId = s.PersonId,
                        Amount = s.Amount
                    });

                    await conn.ExecuteAsync(insertSplit, splitEntities, transaction: trans);
                }

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            _logger.LogInformation("Updated expense {ExpenseId}", expenseId);
            await _personDataService.RecalculateTripBalancesAsync(existing.TripId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating expense: {ExpenseId}", expenseId);
            throw;
        }
    }

    public async Task<bool> DeleteExpenseAsync(string expenseId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var existing = await GetExpenseByIdAsync(expenseId);
            if (existing == null) return false;

            using var trans = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync("DELETE FROM ExpenseSplits WHERE expense_id = @ExpenseId", new { ExpenseId = expenseId }, transaction: trans);
                var rows = await conn.ExecuteAsync("DELETE FROM Expenses WHERE expense_id = @ExpenseId", new { ExpenseId = expenseId }, transaction: trans);
                trans.Commit();

                _logger.LogInformation("Deleted expense {ExpenseId}", expenseId);
                await _personDataService.RecalculateTripBalancesAsync(existing.TripId);
                return rows > 0;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting expense: {ExpenseId}", expenseId);
            throw;
        }
    }

    public async Task<List<ExpenseSplitItemDto>> CalculateDietarySplitsAsync(string tripId, decimal totalAmount, int categoryIndex, List<string> selectedPersonIds)
    {
        try
        {
            var allPersons = (await _personDataService.GetPersonsByTripAsync(tripId)).ToList();
            var targetPersons = selectedPersonIds.Count > 0 
                ? allPersons.Where(p => selectedPersonIds.Contains(p.PersonId)).ToList() 
                : allPersons;

            if (targetPersons.Count == 0 || totalAmount <= 0) return [];

            List<PersonDto> eligible;
            if (categoryIndex == 1) // NonVeg
            {
                eligible = targetPersons.Where(p => (int)p.DietType >= 1).ToList();
                if (eligible.Count == 0) eligible = targetPersons;
            }
            else if (categoryIndex == 2) // Alcohol
            {
                eligible = targetPersons.Where(p => (int)p.DietType == 2).ToList();
                if (eligible.Count == 0) eligible = targetPersons;
            }
            else
            {
                eligible = targetPersons;
            }

            var share = Math.Round(totalAmount / eligible.Count, 2);
            return eligible.Select(p => new ExpenseSplitItemDto
            {
                PersonId = p.PersonId,
                Amount = share
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating dietary splits for trip: {TripId}", tripId);
            throw;
        }
    }
}
