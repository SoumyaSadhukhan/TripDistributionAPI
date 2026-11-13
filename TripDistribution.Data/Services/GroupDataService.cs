using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Groups;
using TripDistribution.Model.Entities;

namespace TripDistribution.Data.Services;

public interface IGroupDataService
{
    Task<IEnumerable<GroupDto>> GetGroupsByTripAsync(string tripId);
    Task<GroupDto?> GetGroupByIdAsync(string groupId);
    Task<Group> CreateGroupAsync(CreateGroupDto dto);
    Task<bool> UpdateGroupAsync(string groupId, string name);
    Task<bool> DeleteGroupAsync(string groupId);
}

public class GroupDataService : IGroupDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<GroupDataService> _logger;

    public GroupDataService(IDbConnectionFactory connectionFactory, ILogger<GroupDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<GroupDto>> GetGroupsByTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT group_id AS GroupId, trip_id AS TripId, name AS Name, created_at AS CreatedAt
                FROM Groups 
                WHERE trip_id = @TripId 
                ORDER BY created_at ASC";

            return await conn.QueryAsync<GroupDto>(sql, new { TripId = tripId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting groups for trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task<GroupDto?> GetGroupByIdAsync(string groupId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT group_id AS GroupId, trip_id AS TripId, name AS Name, created_at AS CreatedAt
                FROM Groups 
                WHERE group_id = @GroupId";

            return await conn.QuerySingleOrDefaultAsync<GroupDto>(sql, new { GroupId = groupId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting group by ID: {GroupId}", groupId);
            throw;
        }
    }

    public async Task<Group> CreateGroupAsync(CreateGroupDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var groupId = Guid.NewGuid().ToString();
            const string sql = @"
                INSERT INTO Groups (group_id, trip_id, name, created_at)
                VALUES (@GroupId, @TripId, @Name, GETDATE())";

            await conn.ExecuteAsync(sql, new { GroupId = groupId, dto.TripId, dto.Name });
            _logger.LogInformation("Group {GroupId} '{Name}' created for trip {TripId}", groupId, dto.Name, dto.TripId);

            return new Group
            {
                GroupId = groupId,
                TripId = dto.TripId,
                Name = dto.Name,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group '{Name}' for trip: {TripId}", dto.Name, dto.TripId);
            throw;
        }
    }

    public async Task<bool> UpdateGroupAsync(string groupId, string name)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = "UPDATE Groups SET name = @Name WHERE group_id = @GroupId";
            var rows = await conn.ExecuteAsync(sql, new { GroupId = groupId, Name = name });
            _logger.LogInformation("Group {GroupId} updated to '{Name}'", groupId, name);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating group {GroupId}", groupId);
            throw;
        }
    }

    public async Task<bool> DeleteGroupAsync(string groupId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            await conn.ExecuteAsync("UPDATE Persons SET group_id = NULL WHERE group_id = @GroupId", new { GroupId = groupId });
            var rows = await conn.ExecuteAsync("DELETE FROM Groups WHERE group_id = @GroupId", new { GroupId = groupId });
            _logger.LogInformation("Group {GroupId} deleted", groupId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting group {GroupId}", groupId);
            throw;
        }
    }
}
