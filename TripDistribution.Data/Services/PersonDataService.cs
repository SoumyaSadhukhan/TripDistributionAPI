using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Persons;
using TripDistribution.Model.Entities;
using TripDistribution.Model.Enums;

namespace TripDistribution.Data.Services;

public interface IPersonDataService
{
    Task<IEnumerable<PersonDto>> GetPersonsByTripAsync(string tripId);
    Task<PersonDto?> GetPersonByIdAsync(string personId);
    Task<Person> CreatePersonAsync(CreatePersonDto dto);
    Task<bool> UpdatePersonAsync(string personId, UpdatePersonDto dto);
    Task<bool> DeletePersonAsync(string personId);
    Task RecalculateTripBalancesAsync(string tripId);
}

public class PersonDataService : IPersonDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<PersonDataService> _logger;

    public PersonDataService(IDbConnectionFactory connectionFactory, ILogger<PersonDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<PersonDto>> GetPersonsByTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT person_id AS PersonId, group_id AS GroupId, trip_id AS TripId,
                       user_id AS UserId, phone_number AS PhoneNumber, name AS Name,
                       diet_type AS DietType, diet_name AS DietName,
                       paid_amount AS PaidAmount, owed_amount AS OwedAmount, balance AS Balance,
                       created_at AS CreatedAt
                FROM Persons 
                WHERE trip_id = @TripId 
                ORDER BY created_at ASC";

            return await conn.QueryAsync<PersonDto>(sql, new { TripId = tripId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting persons for trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task<PersonDto?> GetPersonByIdAsync(string personId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT person_id AS PersonId, group_id AS GroupId, trip_id AS TripId,
                       user_id AS UserId, phone_number AS PhoneNumber, name AS Name,
                       diet_type AS DietType, diet_name AS DietName,
                       paid_amount AS PaidAmount, owed_amount AS OwedAmount, balance AS Balance,
                       created_at AS CreatedAt
                FROM Persons 
                WHERE person_id = @PersonId";

            return await conn.QuerySingleOrDefaultAsync<PersonDto>(sql, new { PersonId = personId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting person by ID: {PersonId}", personId);
            throw;
        }
    }

    public async Task<Person> CreatePersonAsync(CreatePersonDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var personId = Guid.NewGuid().ToString();
            var dietName = !string.IsNullOrWhiteSpace(dto.DietName) ? dto.DietName : dto.DietType.ToString();

            const string sql = @"
                INSERT INTO Persons (person_id, group_id, trip_id, user_id, phone_number, name, diet_type, diet_name, paid_amount, owed_amount, balance, created_at)
                VALUES (@PersonId, @GroupId, @TripId, @UserId, @PhoneNumber, @Name, @DietType, @DietName, 0.00, 0.00, 0.00, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                PersonId = personId,
                dto.GroupId,
                dto.TripId,
                dto.UserId,
                dto.PhoneNumber,
                dto.Name,
                DietType = (int)dto.DietType,
                DietName = dietName
            });

            if (!string.IsNullOrWhiteSpace(dto.UserId) || !string.IsNullOrWhiteSpace(dto.PhoneNumber))
            {
                var collabId = Guid.NewGuid().ToString();
                await conn.ExecuteAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM TripCollaborators WHERE trip_id = @TripId AND (user_id = @UserId OR phone_number = @PhoneNumber))
                    BEGIN
                        INSERT INTO TripCollaborators (collab_id, trip_id, user_id, person_id, phone_number, role, status, joined_at)
                        VALUES (@CollabId, @TripId, @UserId, @PersonId, @PhoneNumber, 'MEMBER', 'ACCEPTED', GETDATE())
                    END",
                    new
                    {
                        CollabId = collabId,
                        dto.TripId,
                        dto.UserId,
                        PersonId = personId,
                        dto.PhoneNumber
                    });
            }

            _logger.LogInformation("Member {PersonId} '{Name}' created in trip {TripId}", personId, dto.Name, dto.TripId);

            return new Person
            {
                PersonId = personId,
                GroupId = dto.GroupId,
                TripId = dto.TripId,
                UserId = dto.UserId,
                PhoneNumber = dto.PhoneNumber,
                Name = dto.Name,
                DietType = dto.DietType,
                DietName = dietName,
                PaidAmount = 0,
                OwedAmount = 0,
                Balance = 0,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating person '{Name}' in trip: {TripId}", dto.Name, dto.TripId);
            throw;
        }
    }

    public async Task<bool> UpdatePersonAsync(string personId, UpdatePersonDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var dietName = !string.IsNullOrWhiteSpace(dto.DietName)
                ? dto.DietName
                : (dto.DietType switch
                {
                    DietType.NonVegetarian => "Non-Vegetarian",
                    DietType.NonVegAlcoholic => "Non-Veg + Alcohol",
                    _ => "Vegetarian"
                });

            const string sql = @"
                UPDATE Persons 
                SET name = COALESCE(@Name, name),
                    group_id = COALESCE(@GroupId, group_id),
                    phone_number = COALESCE(@PhoneNumber, phone_number),
                    diet_type = @DietType,
                    diet_name = @DietName
                WHERE person_id = @PersonId;

                UPDATE u
                SET u.diet_type = @DietType, u.diet_name = @DietName
                FROM Users u
                JOIN Persons p ON p.user_id = u.user_id
                WHERE p.person_id = @PersonId;";

            var rows = await conn.ExecuteAsync(sql, new
            {
                PersonId = personId,
                dto.Name,
                dto.GroupId,
                dto.PhoneNumber,
                DietType = (int)dto.DietType,
                DietName = dietName
            });

            _logger.LogInformation("Member {PersonId} updated to '{Name}', diet {DietType}", personId, dto.Name, dto.DietType);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating person: {PersonId}", personId);
            throw;
        }
    }

    public async Task<bool> DeletePersonAsync(string personId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var person = await GetPersonByIdAsync(personId);
            if (person == null) return false;

            using var trans = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync("DELETE FROM ExpenseSplits WHERE person_id = @PersonId", new { PersonId = personId }, transaction: trans);
                await conn.ExecuteAsync("DELETE FROM SettlementTransactions WHERE from_person_id = @PersonId OR to_person_id = @PersonId", new { PersonId = personId }, transaction: trans);
                await conn.ExecuteAsync("DELETE FROM TripCollaborators WHERE person_id = @PersonId", new { PersonId = personId }, transaction: trans);
                var rows = await conn.ExecuteAsync("DELETE FROM Persons WHERE person_id = @PersonId", new { PersonId = personId }, transaction: trans);
                trans.Commit();

                _logger.LogInformation("Person {PersonId} deleted, recalculating balances for trip {TripId}", personId, person.TripId);
                await RecalculateTripBalancesAsync(person.TripId);
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
            _logger.LogError(ex, "Error deleting person: {PersonId}", personId);
            throw;
        }
    }

    public async Task RecalculateTripBalancesAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                -- 1. Reset all persons in the trip
                UPDATE Persons 
                SET paid_amount = 0.00, owed_amount = 0.00, balance = 0.00
                WHERE trip_id = @TripId;

                -- 2. Update paid_amount from Expenses
                UPDATE p
                SET p.paid_amount = ISNULL(exp.TotalPaid, 0.00)
                FROM Persons p
                INNER JOIN (
                    SELECT paid_by_person_id, SUM(amount) AS TotalPaid
                    FROM Expenses
                    WHERE trip_id = @TripId
                    GROUP BY paid_by_person_id
                ) exp ON p.person_id = exp.paid_by_person_id
                WHERE p.trip_id = @TripId;

                -- 3. Update owed_amount from ExpenseSplits
                UPDATE p
                SET p.owed_amount = ISNULL(sp.TotalOwed, 0.00)
                FROM Persons p
                INNER JOIN (
                    SELECT person_id, SUM(amount) AS TotalOwed
                    FROM ExpenseSplits
                    WHERE trip_id = @TripId
                    GROUP BY person_id
                ) sp ON p.person_id = sp.person_id
                WHERE p.trip_id = @TripId;

                -- 4. Factor in Completed Settlements
                UPDATE p
                SET p.paid_amount = p.paid_amount + ISNULL(settleFrom.TotalSettled, 0.00)
                FROM Persons p
                INNER JOIN (
                    SELECT from_person_id, SUM(amount) AS TotalSettled
                    FROM SettlementTransactions
                    WHERE trip_id = @TripId AND status IN ('ACCEPTED', 'COMPLETED') AND is_completed = 1
                    GROUP BY from_person_id
                ) settleFrom ON p.person_id = settleFrom.from_person_id
                WHERE p.trip_id = @TripId;

                UPDATE p
                SET p.owed_amount = p.owed_amount + ISNULL(settleTo.TotalReceived, 0.00)
                FROM Persons p
                INNER JOIN (
                    SELECT to_person_id, SUM(amount) AS TotalReceived
                    FROM SettlementTransactions
                    WHERE trip_id = @TripId AND status IN ('ACCEPTED', 'COMPLETED') AND is_completed = 1
                    GROUP BY to_person_id
                ) settleTo ON p.person_id = settleTo.to_person_id
                WHERE p.trip_id = @TripId;

                -- 5. Calculate net balance (Paid - Owed)
                UPDATE Persons
                SET balance = paid_amount - owed_amount
                WHERE trip_id = @TripId;
            ";

            await conn.ExecuteAsync(sql, new { TripId = tripId });
            _logger.LogInformation("Balances recalculated for trip: {TripId}", tripId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating balances for trip: {TripId}", tripId);
            throw;
        }
    }
}
