using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Settlements;
using TripDistribution.Model.Entities;

namespace TripDistribution.Data.Services;

public interface ISettlementDataService
{
    Task<IEnumerable<SettlementDto>> GetSettlementsByTripAsync(string tripId);
    Task<SettlementDto?> GetSettlementByIdAsync(string settlementId);
    Task<SettlementDto> ProposeSettlementAsync(string createdByUserId, ProposeSettlementDto dto);
    Task<bool> UpdateSettlementStatusAsync(string settlementId, string status);
    Task<bool> DeleteSettlementAsync(string settlementId);
}

public class SettlementDataService : ISettlementDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPersonDataService _personDataService;
    private readonly ILogger<SettlementDataService> _logger;

    public SettlementDataService(
        IDbConnectionFactory connectionFactory, 
        IPersonDataService personDataService,
        ILogger<SettlementDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _personDataService = personDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<SettlementDto>> GetSettlementsByTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
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

            return await conn.QueryAsync<SettlementDto>(sql, new { TripId = tripId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting settlements for trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task<SettlementDto?> GetSettlementByIdAsync(string settlementId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT s.settlement_id AS SettlementId, s.trip_id AS TripId,
                       s.from_person_id AS FromPersonId, p1.name AS FromPersonName,
                       s.to_person_id AS ToPersonId, p2.name AS ToPersonName,
                       s.amount AS Amount, s.is_completed AS IsCompleted, s.status AS Status,
                       s.created_by_user_id AS CreatedByUserId, s.settled_at AS SettledAt
                FROM SettlementTransactions s
                LEFT JOIN Persons p1 ON s.from_person_id = p1.person_id
                LEFT JOIN Persons p2 ON s.to_person_id = p2.person_id
                WHERE s.settlement_id = @SettlementId";

            return await conn.QuerySingleOrDefaultAsync<SettlementDto>(sql, new { SettlementId = settlementId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting settlement by ID: {SettlementId}", settlementId);
            throw;
        }
    }

    public async Task<SettlementDto> ProposeSettlementAsync(string createdByUserId, ProposeSettlementDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var settlementId = Guid.NewGuid().ToString();

            const string sql = @"
                INSERT INTO SettlementTransactions (settlement_id, trip_id, from_person_id, to_person_id, amount, is_completed, status, created_by_user_id, settled_at)
                VALUES (@SettlementId, @TripId, @FromPersonId, @ToPersonId, @Amount, 1, 'ACCEPTED', @CreatedByUserId, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                SettlementId = settlementId,
                dto.TripId,
                dto.FromPersonId,
                dto.ToPersonId,
                dto.Amount,
                CreatedByUserId = createdByUserId
            });

            _logger.LogInformation("Settlement {SettlementId} recorded for trip {TripId}, amount {Amount}", settlementId, dto.TripId, dto.Amount);

            await _personDataService.RecalculateTripBalancesAsync(dto.TripId);
            return (await GetSettlementByIdAsync(settlementId))!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording settlement for trip: {TripId}", dto.TripId);
            throw;
        }
    }

    public async Task<bool> UpdateSettlementStatusAsync(string settlementId, string status)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var existing = await GetSettlementByIdAsync(settlementId);
            if (existing == null) return false;

            var isCompleted = status.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase) || status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);
            const string sql = @"
                UPDATE SettlementTransactions 
                SET status = @Status, is_completed = @IsCompleted
                WHERE settlement_id = @SettlementId";

            var rows = await conn.ExecuteAsync(sql, new { SettlementId = settlementId, Status = status.ToUpper(), IsCompleted = isCompleted });

            _logger.LogInformation("Settlement {SettlementId} status updated to {Status}", settlementId, status);
            await _personDataService.RecalculateTripBalancesAsync(existing.TripId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating settlement status: {SettlementId}", settlementId);
            throw;
        }
    }

    public async Task<bool> DeleteSettlementAsync(string settlementId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var existing = await GetSettlementByIdAsync(settlementId);
            if (existing == null) return false;

            var rows = await conn.ExecuteAsync("DELETE FROM SettlementTransactions WHERE settlement_id = @SettlementId", new { SettlementId = settlementId });
            _logger.LogInformation("Settlement {SettlementId} deleted", settlementId);
            await _personDataService.RecalculateTripBalancesAsync(existing.TripId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting settlement: {SettlementId}", settlementId);
            throw;
        }
    }
}
