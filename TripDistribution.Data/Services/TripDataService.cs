using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Expenses;
using TripDistribution.Model.DTOs.Groups;
using TripDistribution.Model.DTOs.Persons;
using TripDistribution.Model.DTOs.Settlements;
using TripDistribution.Model.DTOs.Trips;
using TripDistribution.Model.Entities;
using TripDistribution.Model.Enums;

namespace TripDistribution.Data.Services;

public interface ITripDataService
{
    Task<IEnumerable<TripSummaryDto>> GetUserTripsAsync(string userId, string? phone);
    Task<TripDetailDto?> GetTripDetailAsync(string tripId, string currentUserId);
    Task<Trip> CreateTripAsync(string userId, string? userPhone, string? userName, DietType dietType, string? dietName, CreateTripDto dto);
    Task<bool> UpdateTripAsync(string tripId, UpdateTripDto dto);
    Task<bool> DeleteTripAsync(string tripId);
}

public class TripDataService : ITripDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TripDataService> _logger;

    public TripDataService(IDbConnectionFactory connectionFactory, ILogger<TripDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<TripSummaryDto>> GetUserTripsAsync(string userId, string? phone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = !string.IsNullOrWhiteSpace(phone) ? phone.Replace(" ", "").Trim() : "";
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            const string sql = @"
                SELECT DISTINCT t.trip_id AS TripId, t.user_id AS UserId, t.name AS Name, 
                       t.description AS Description, t.currency_code AS CurrencyCode,
                       t.created_at AS CreatedAt, t.updated_at AS UpdatedAt,
                       CASE WHEN t.user_id = @UserId THEN 1 ELSE 0 END AS IsOwner,
                       COALESCE(tc.role, CASE WHEN t.user_id = @UserId THEN 'OWNER' ELSE 'MEMBER' END) AS Role
                FROM Trips t
                LEFT JOIN Persons p ON t.trip_id = p.trip_id
                LEFT JOIN TripCollaborators tc ON t.trip_id = tc.trip_id AND (tc.user_id = @UserId OR (@Last10 <> '' AND RIGHT(tc.phone_number, 10) = @Last10))
                WHERE t.user_id = @UserId 
                   OR p.user_id = @UserId 
                   OR tc.user_id = @UserId
                   OR (@Last10 <> '' AND (RIGHT(p.phone_number, 10) = @Last10 OR RIGHT(tc.phone_number, 10) = @Last10))
                ORDER BY t.created_at DESC";

            var trips = (await conn.QueryAsync<TripSummaryDto>(sql, new { UserId = userId, Last10 = last10 })).ToList();
            if (trips.Count == 0) return trips;

            foreach (var t in trips)
            {
                var stats = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(DISTINCT p.person_id) AS MemberCount,
                        ISNULL(SUM(e.amount), 0) AS TotalExpenses
                    FROM Trips tr
                    LEFT JOIN Persons p ON tr.trip_id = p.trip_id
                    LEFT JOIN Expenses e ON tr.trip_id = e.trip_id
                    WHERE tr.trip_id = @TripId", new { t.TripId });

                if (stats != null)
                {
                    t.MemberCount = (int)(stats.MemberCount ?? 0);
                    t.TotalExpenses = (decimal)(stats.TotalExpenses ?? 0.00m);
                }

                var myPerson = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
                    SELECT TOP 1 paid_amount, owed_amount, balance
                    FROM Persons
                    WHERE trip_id = @TripId AND (user_id = @UserId OR (@Last10 <> '' AND RIGHT(phone_number, 10) = @Last10))",
                    new { t.TripId, UserId = userId, Last10 = last10 });

                if (myPerson != null)
                {
                    t.MyPaidAmount = (decimal)(myPerson.paid_amount ?? 0.00m);
                    t.MyOwedAmount = (decimal)(myPerson.owed_amount ?? 0.00m);
                    t.MyBalance = (decimal)(myPerson.balance ?? 0.00m);
                }
            }

            return trips;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user trips for userId: {UserId}", userId);
            throw;
        }
    }

    public async Task<TripDetailDto?> GetTripDetailAsync(string tripId, string currentUserId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string tripSql = @"
                SELECT trip_id AS TripId, user_id AS UserId, name AS Name, 
                       description AS Description, currency_code AS CurrencyCode,
                       created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM Trips 
                WHERE trip_id = @TripId";

            var trip = await conn.QuerySingleOrDefaultAsync<TripDetailDto>(tripSql, new { TripId = tripId });
            if (trip == null) return null;

            // 1. Groups
            const string groupsSql = @"
                SELECT group_id AS GroupId, trip_id AS TripId, name AS Name, created_at AS CreatedAt
                FROM Groups WHERE trip_id = @TripId ORDER BY created_at ASC";
            trip.Groups = (await conn.QueryAsync<GroupDto>(groupsSql, new { TripId = tripId })).ToList();

            // 2. Persons
            const string personsSql = @"
                SELECT person_id AS PersonId, group_id AS GroupId, trip_id AS TripId,
                       user_id AS UserId, phone_number AS PhoneNumber, name AS Name,
                       diet_type AS DietType, diet_name AS DietName,
                       paid_amount AS PaidAmount, owed_amount AS OwedAmount, balance AS Balance,
                       created_at AS CreatedAt
                FROM Persons WHERE trip_id = @TripId ORDER BY created_at ASC";
            trip.Persons = (await conn.QueryAsync<PersonDto>(personsSql, new { TripId = tripId })).ToList();

            // 3. Expenses with Splits
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
            trip.Expenses = expenses;

            // 4. Settlements
            const string settlementsSql = @"
                SELECT s.settlement_id AS SettlementId, s.trip_id AS TripId,
                       s.from_person_id AS FromPersonId, p1.name AS FromPersonName,
                       s.to_person_id AS ToPersonId, p2.name AS ToPersonName,
                       s.amount AS Amount, s.is_completed AS IsCompleted, s.status AS Status,
                       s.created_by_user_id AS CreatedByUserId, s.settled_at AS SettledAt
                FROM SettlementTransactions s
                LEFT JOIN Persons p1 ON s.from_person_id = p1.person_id
                LEFT JOIN Persons p2 ON s.to_person_id = p2.person_id
                WHERE s.trip_id = @TripId
                ORDER BY s.settled_at DESC";
            trip.Settlements = (await conn.QueryAsync<SettlementDto>(settlementsSql, new { TripId = tripId })).ToList();

            return trip;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trip detail for tripId: {TripId}", tripId);
            throw;
        }
    }

    public async Task<Trip> CreateTripAsync(string userId, string? userPhone, string? userName, DietType dietType, string? dietName, CreateTripDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var tripId = Guid.NewGuid().ToString();
            var groupId = Guid.NewGuid().ToString();
            var personId = Guid.NewGuid().ToString();
            var collabId = Guid.NewGuid().ToString();
            var dName = !string.IsNullOrWhiteSpace(dietName) ? dietName : dietType.ToString();
            var pName = !string.IsNullOrWhiteSpace(userName) ? userName : "Me";

            const string insertTrip = @"
                INSERT INTO Trips (trip_id, user_id, name, description, currency_code, created_at, updated_at)
                VALUES (@TripId, @UserId, @Name, @Description, @CurrencyCode, GETDATE(), GETDATE())";

            await conn.ExecuteAsync(insertTrip, new
            {
                TripId = tripId,
                UserId = userId,
                Name = dto.Name,
                Description = dto.Description,
                CurrencyCode = !string.IsNullOrWhiteSpace(dto.CurrencyCode) ? dto.CurrencyCode : "INR"
            });

            await conn.ExecuteAsync(@"
                INSERT INTO Groups (group_id, trip_id, name, created_at)
                VALUES (@GroupId, @TripId, 'General', GETDATE())",
                new { GroupId = groupId, TripId = tripId });

            await conn.ExecuteAsync(@"
                INSERT INTO Persons (person_id, group_id, trip_id, user_id, phone_number, name, diet_type, diet_name, paid_amount, owed_amount, balance, created_at)
                VALUES (@PersonId, @GroupId, @TripId, @UserId, @Phone, @Name, @DietType, @DietName, 0.00, 0.00, 0.00, GETDATE())",
                new
                {
                    PersonId = personId,
                    GroupId = groupId,
                    TripId = tripId,
                    UserId = userId,
                    Phone = userPhone,
                    Name = pName,
                    DietType = (int)dietType,
                    DietName = dName
                });

            await conn.ExecuteAsync(@"
                INSERT INTO TripCollaborators (collab_id, trip_id, user_id, person_id, phone_number, role, status, joined_at)
                VALUES (@CollabId, @TripId, @UserId, @PersonId, @Phone, 'OWNER', 'ACCEPTED', GETDATE())",
                new
                {
                    CollabId = collabId,
                    TripId = tripId,
                    UserId = userId,
                    PersonId = personId,
                    Phone = userPhone
                });

            _logger.LogInformation("Trip {TripId} created by user {UserId}", tripId, userId);

            return new Trip
            {
                TripId = tripId,
                UserId = userId,
                Name = dto.Name,
                Description = dto.Description,
                CurrencyCode = dto.CurrencyCode,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating trip for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> UpdateTripAsync(string tripId, UpdateTripDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                UPDATE Trips 
                SET name = @Name, description = @Description, currency_code = @CurrencyCode, updated_at = GETDATE()
                WHERE trip_id = @TripId";

            var rows = await conn.ExecuteAsync(sql, new
            {
                TripId = tripId,
                dto.Name,
                dto.Description,
                CurrencyCode = !string.IsNullOrWhiteSpace(dto.CurrencyCode) ? dto.CurrencyCode : "INR"
            });

            _logger.LogInformation("Trip {TripId} updated", tripId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task<bool> DeleteTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            
            await conn.ExecuteAsync("DELETE FROM ExpenseSplits WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM Expenses WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM SettlementTransactions WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM TripCollaborators WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM Persons WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM Groups WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM ActivityLogs WHERE trip_id = @TripId", new { TripId = tripId });
            await conn.ExecuteAsync("DELETE FROM Notifications WHERE trip_id = @TripId", new { TripId = tripId });
            var rows = await conn.ExecuteAsync("DELETE FROM Trips WHERE trip_id = @TripId", new { TripId = tripId });
            
            _logger.LogInformation("Trip {TripId} and all child entities deleted", tripId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting trip: {TripId}", tripId);
            throw;
        }
    }
}
