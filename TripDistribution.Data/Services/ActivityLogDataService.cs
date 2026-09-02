using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.ActivityLogs;

namespace TripDistribution.Data.Services;

public interface IActivityLogDataService
{
    Task<IEnumerable<ActivityLogDto>> GetActivityLogsByTripAsync(string tripId);
    Task LogActivityAsync(string? userId, string? tripId, string actionType, string? details);
}

public class ActivityLogDataService : IActivityLogDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<ActivityLogDataService> _logger;

    public ActivityLogDataService(IDbConnectionFactory connectionFactory, ILogger<ActivityLogDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<ActivityLogDto>> GetActivityLogsByTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT log_id AS LogId, user_id AS UserId, trip_id AS TripId,
                       action_type AS ActionType, details AS Details, created_at AS CreatedAt
                FROM ActivityLogs
                WHERE trip_id = @TripId
                ORDER BY created_at DESC";

            return await conn.QueryAsync<ActivityLogDto>(sql, new { TripId = tripId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting activity logs for trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task LogActivityAsync(string? userId, string? tripId, string actionType, string? details)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO ActivityLogs (log_id, user_id, trip_id, action_type, details, created_at)
                VALUES (@LogId, @UserId, @TripId, @ActionType, @Details, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                LogId = Guid.NewGuid().ToString(),
                UserId = userId,
                TripId = tripId,
                ActionType = actionType,
                Details = details
            });

            _logger.LogInformation("Activity logged: {ActionType} for trip: {TripId} by user: {UserId}", actionType, tripId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging activity: {ActionType}", actionType);
            // Non-critical, do not rethrow to avoid breaking primary workflows
        }
    }
}
