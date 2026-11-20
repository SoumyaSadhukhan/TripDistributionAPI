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
    Task<bool> SyncTripsAsync(string userId, string jsonTrips);
    Task<bool> HasUserTripAccessAsync(string tripId, string userId, string? phone = null);
    Task<bool> CanUserManageTripAsync(string tripId, string userId);
    Task<bool> IsTripOwnerAsync(string tripId, string userId);
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
                WITH ExpSummary AS (
                    SELECT trip_id, SUM(amount) AS TotalExpenses
                    FROM Expenses
                    GROUP BY trip_id
                ),
                MemberSummary AS (
                    SELECT trip_id, COUNT(DISTINCT person_id) AS MemberCount
                    FROM Persons
                    GROUP BY trip_id
                ),
                MyPersonSummary AS (
                    SELECT trip_id, paid_amount, owed_amount, balance,
                           ROW_NUMBER() OVER (PARTITION BY trip_id ORDER BY created_at ASC) as rn
                    FROM Persons
                    WHERE user_id = @UserId OR (@Last10 <> '' AND RIGHT(phone_number, 10) = @Last10)
                )
                SELECT DISTINCT 
                       t.trip_id AS TripId, t.user_id AS UserId, t.name AS Name, 
                       t.description AS Description, t.currency_code AS CurrencyCode,
                       t.created_at AS CreatedAt, t.updated_at AS UpdatedAt,
                       CASE WHEN t.user_id = @UserId THEN 1 ELSE 0 END AS IsOwner,
                       COALESCE(tc.role, CASE WHEN t.user_id = @UserId THEN 'OWNER' ELSE 'MEMBER' END) AS Role,
                       ISNULL(ms.MemberCount, 0) AS MemberCount,
                       ISNULL(es.TotalExpenses, 0.00) AS TotalExpenses,
                       ISNULL(mps.paid_amount, 0.00) AS MyPaidAmount,
                       ISNULL(mps.owed_amount, 0.00) AS MyOwedAmount,
                       ISNULL(mps.balance, 0.00) AS MyBalance
                FROM Trips t
                LEFT JOIN Persons p ON t.trip_id = p.trip_id
                LEFT JOIN TripCollaborators tc ON t.trip_id = tc.trip_id AND (tc.user_id = @UserId OR (@Last10 <> '' AND RIGHT(tc.phone_number, 10) = @Last10))
                LEFT JOIN MemberSummary ms ON t.trip_id = ms.trip_id
                LEFT JOIN ExpSummary es ON t.trip_id = es.trip_id
                LEFT JOIN MyPersonSummary mps ON t.trip_id = mps.trip_id AND mps.rn = 1
                WHERE t.user_id = @UserId 
                   OR (tc.user_id = @UserId AND tc.status = 'ACCEPTED')
                   OR (p.user_id = @UserId AND NOT EXISTS (
                           SELECT 1 FROM TripCollaborators tc_p 
                           WHERE tc_p.trip_id = t.trip_id 
                             AND (tc_p.user_id = @UserId OR (@Last10 <> '' AND RIGHT(tc_p.phone_number, 10) = @Last10))
                             AND tc_p.status = 'PENDING'
                       ))
                   OR (@Last10 <> '' AND RIGHT(tc.phone_number, 10) = @Last10 AND tc.status = 'ACCEPTED')
                   OR (@Last10 <> '' AND RIGHT(p.phone_number, 10) = @Last10 AND NOT EXISTS (
                           SELECT 1 FROM TripCollaborators tc_p 
                           WHERE tc_p.trip_id = t.trip_id 
                             AND (tc_p.user_id = @UserId OR RIGHT(tc_p.phone_number, 10) = @Last10)
                             AND tc_p.status = 'PENDING'
                       ))
                ORDER BY t.created_at DESC";

            var trips = await conn.QueryAsync<TripSummaryDto>(sql, new { UserId = userId, Last10 = last10 });
            return trips.ToList();
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
            var tripId = !string.IsNullOrWhiteSpace(dto.TripId) ? dto.TripId : Guid.NewGuid().ToString();
            var groupId = Guid.NewGuid().ToString();
            var personId = Guid.NewGuid().ToString();
            var collabId = Guid.NewGuid().ToString();
            var dName = !string.IsNullOrWhiteSpace(dietName) ? dietName : dietType.ToString();
            var pName = !string.IsNullOrWhiteSpace(userName) ? userName : "Me";

            using var trans = conn.BeginTransaction();
            try
            {
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
                }, trans);

                await conn.ExecuteAsync(@"
                    INSERT INTO Groups (group_id, trip_id, name, created_at)
                    VALUES (@GroupId, @TripId, 'General', GETDATE())",
                    new { GroupId = groupId, TripId = tripId }, trans);

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
                    }, trans);

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
                    }, trans);

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

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

    public async Task<bool> SyncTripsAsync(string userId, string jsonTrips)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
BEGIN TRY
    BEGIN TRANSACTION;

    SELECT 
        JSON_VALUE(value, '$.id') AS trip_id,
        JSON_VALUE(value, '$.name') AS name,
        JSON_VALUE(value, '$.description') AS description,
        ISNULL(JSON_VALUE(value, '$.currencyCode'), 'INR') AS currency_code
    INTO #TempTrips
    FROM OPENJSON(@Json);

    MERGE INTO Trips AS Target
    USING #TempTrips AS Source
    ON (Target.trip_id = Source.trip_id)
    WHEN MATCHED THEN
        UPDATE SET 
            name = Source.name, 
            description = Source.description,
            currency_code = Source.currency_code,
            updated_at = GETDATE()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (trip_id, user_id, name, description, currency_code, created_at, updated_at)
        VALUES (Source.trip_id, @UserId, Source.name, Source.description, Source.currency_code, GETDATE(), GETDATE());

    SELECT 
        JSON_VALUE(t.value, '$.id') AS trip_id,
        JSON_VALUE(g.value, '$.id') AS group_id,
        JSON_VALUE(g.value, '$.name') AS name
    INTO #TempGroups
    FROM OPENJSON(@Json) t
    CROSS APPLY OPENJSON(t.value, '$.groups') g;

    MERGE INTO Groups AS Target
    USING #TempGroups AS Source
    ON (Target.group_id = Source.group_id)
    WHEN MATCHED THEN
        UPDATE SET name = Source.name
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (group_id, trip_id, name, created_at)
        VALUES (Source.group_id, Source.trip_id, Source.name, GETDATE());

    SELECT 
        JSON_VALUE(t.value, '$.id') AS trip_id,
        JSON_VALUE(g.value, '$.id') AS group_id,
        JSON_VALUE(p.value, '$.id') AS person_id,
        JSON_VALUE(p.value, '$.userId') AS user_id,
        JSON_VALUE(p.value, '$.phoneNumber') AS phone_number,
        JSON_VALUE(p.value, '$.name') AS name,
        CAST(ISNULL(JSON_VALUE(p.value, '$.dietType'), '0') AS INT) AS diet_type,
        ISNULL(JSON_VALUE(p.value, '$.dietName'), 'Vegetarian') AS diet_name,
        CAST(ISNULL(JSON_VALUE(p.value, '$.paidAmount'), '0') AS DECIMAL(18,2)) AS paid_amount,
        CAST(ISNULL(JSON_VALUE(p.value, '$.owedAmount'), '0') AS DECIMAL(18,2)) AS owed_amount,
        CAST(ISNULL(JSON_VALUE(p.value, '$.balance'), '0') AS DECIMAL(18,2)) AS balance
    INTO #TempPersons
    FROM OPENJSON(@Json) t
    CROSS APPLY OPENJSON(t.value, '$.groups') g
    CROSS APPLY OPENJSON(g.value, '$.members') p;

    MERGE INTO Persons AS Target
    USING #TempPersons AS Source
    ON (Target.person_id = Source.person_id)
    WHEN MATCHED THEN
        UPDATE SET 
            group_id = Source.group_id,
            user_id = Source.user_id,
            phone_number = Source.phone_number,
            name = Source.name,
            diet_type = Source.diet_type,
            diet_name = Source.diet_name,
            paid_amount = Source.paid_amount,
            owed_amount = Source.owed_amount,
            balance = Source.balance
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (person_id, group_id, trip_id, user_id, phone_number, name, diet_type, diet_name, paid_amount, owed_amount, balance, created_at)
        VALUES (Source.person_id, Source.group_id, Source.trip_id, Source.user_id, Source.phone_number, Source.name, Source.diet_type, Source.diet_name, Source.paid_amount, Source.owed_amount, Source.balance, GETDATE());

    SELECT 
        JSON_VALUE(t.value, '$.id') AS trip_id,
        JSON_VALUE(e.value, '$.id') AS expense_id,
        JSON_VALUE(e.value, '$.description') AS description,
        CAST(ISNULL(JSON_VALUE(e.value, '$.amount'), '0') AS DECIMAL(18,2)) AS amount,
        ISNULL(JSON_VALUE(e.value, '$.currencyCode'), 'INR') AS currency_code,
        CAST(ISNULL(JSON_VALUE(e.value, '$.categoryIndex'), '0') AS INT) AS category_index,
        ISNULL(JSON_VALUE(e.value, '$.categoryName'), 'General') AS category_name,
        JSON_VALUE(e.value, '$.paidByPersonId') AS paid_by_person_id,
        ISNULL(JSON_VALUE(e.value, '$.splitType'), 'EQUAL') AS split_type,
        ISNULL(CAST(JSON_VALUE(e.value, '$.date') AS DATETIME), GETDATE()) AS expense_date
    INTO #TempExpenses
    FROM OPENJSON(@Json) t
    CROSS APPLY OPENJSON(t.value, '$.expenses') e;

    MERGE INTO Expenses AS Target
    USING #TempExpenses AS Source
    ON (Target.expense_id = Source.expense_id)
    WHEN MATCHED THEN
        UPDATE SET 
            description = Source.description,
            amount = Source.amount,
            currency_code = Source.currency_code,
            category_index = Source.category_index,
            category_name = Source.category_name,
            paid_by_person_id = Source.paid_by_person_id,
            split_type = Source.split_type,
            expense_date = Source.expense_date
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (expense_id, trip_id, description, amount, currency_code, category_index, category_name, paid_by_person_id, split_type, expense_date, created_at)
        VALUES (Source.expense_id, Source.trip_id, Source.description, Source.amount, Source.currency_code, Source.category_index, Source.category_name, Source.paid_by_person_id, Source.split_type, Source.expense_date, GETDATE());

    SELECT 
        JSON_VALUE(t.value, '$.id') AS trip_id,
        JSON_VALUE(e.value, '$.id') AS expense_id,
        JSON_VALUE(s.value, '$.id') AS split_id,
        JSON_VALUE(s.value, '$.personId') AS person_id,
        CAST(ISNULL(JSON_VALUE(s.value, '$.amount'), '0') AS DECIMAL(18,2)) AS amount
    INTO #TempSplits
    FROM OPENJSON(@Json) t
    CROSS APPLY OPENJSON(t.value, '$.expenses') e
    CROSS APPLY OPENJSON(e.value, '$.splits') s;

    MERGE INTO ExpenseSplits AS Target
    USING #TempSplits AS Source
    ON (Target.split_id = Source.split_id)
    WHEN MATCHED THEN
        UPDATE SET 
            amount = Source.amount
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (split_id, expense_id, trip_id, person_id, amount, created_at)
        VALUES (Source.split_id, Source.expense_id, Source.trip_id, Source.person_id, Source.amount, GETDATE());

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;";

            await conn.ExecuteAsync(sql, new { UserId = userId, Json = jsonTrips });
            _logger.LogInformation("Synced trips for user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in SyncTripsAsync for user: {UserId}", userId);
            return false;
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

    public async Task<bool> HasUserTripAccessAsync(string tripId, string userId, string? phone = null)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = !string.IsNullOrWhiteSpace(phone) ? phone.Replace(" ", "").Trim() : "";
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            const string sql = @"
                SELECT COUNT(1)
                FROM Trips t
                LEFT JOIN Persons p ON t.trip_id = p.trip_id
                LEFT JOIN TripCollaborators tc ON t.trip_id = tc.trip_id
                WHERE t.trip_id = @TripId 
                  AND (t.user_id = @UserId 
                       OR (tc.user_id = @UserId AND tc.status = 'ACCEPTED')
                       OR (p.user_id = @UserId AND NOT EXISTS (
                               SELECT 1 FROM TripCollaborators tc_p 
                               WHERE tc_p.trip_id = t.trip_id 
                                 AND (tc_p.user_id = @UserId OR (@Last10 <> '' AND RIGHT(tc_p.phone_number, 10) = @Last10))
                                 AND tc_p.status = 'PENDING'
                           ))
                       OR (@Last10 <> '' AND RIGHT(tc.phone_number, 10) = @Last10 AND tc.status = 'ACCEPTED')
                       OR (@Last10 <> '' AND RIGHT(p.phone_number, 10) = @Last10 AND NOT EXISTS (
                               SELECT 1 FROM TripCollaborators tc_p 
                               WHERE tc_p.trip_id = t.trip_id 
                                 AND (tc_p.user_id = @UserId OR RIGHT(tc_p.phone_number, 10) = @Last10)
                                 AND tc_p.status = 'PENDING'
                           )))";

            var count = await conn.ExecuteScalarAsync<int>(sql, new { TripId = tripId, UserId = userId, Last10 = last10 });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking trip access for trip: {TripId}, user: {UserId}", tripId, userId);
            return false;
        }
    }

    public async Task<bool> CanUserManageTripAsync(string tripId, string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT COUNT(1)
                FROM Trips t
                LEFT JOIN TripCollaborators tc ON t.trip_id = tc.trip_id AND tc.user_id = @UserId
                WHERE t.trip_id = @TripId 
                  AND (t.user_id = @UserId OR tc.role IN ('OWNER', 'ADMIN', 'EDITOR'))";

            var count = await conn.ExecuteScalarAsync<int>(sql, new { TripId = tripId, UserId = userId });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking manage access for trip: {TripId}, user: {UserId}", tripId, userId);
            return false;
        }
    }

    public async Task<bool> IsTripOwnerAsync(string tripId, string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = "SELECT COUNT(1) FROM Trips WHERE trip_id = @TripId AND user_id = @UserId";
            var count = await conn.ExecuteScalarAsync<int>(sql, new { TripId = tripId, UserId = userId });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking trip ownership for trip: {TripId}, user: {UserId}", tripId, userId);
            return false;
        }
    }
}
