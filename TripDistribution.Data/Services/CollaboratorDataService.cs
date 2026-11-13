using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Collaborators;
using TripDistribution.Model.Entities;

namespace TripDistribution.Data.Services;

public interface ICollaboratorDataService
{
    Task<IEnumerable<CollaboratorDto>> GetCollaboratorsByTripAsync(string tripId);
    Task<CollaboratorDto?> GetCollaboratorByIdAsync(string collabId);
    Task<CollaboratorDto> InviteCollaboratorAsync(InviteCollaboratorDto dto);
    Task<bool> UpdateCollaboratorRoleAsync(string collabId, string role);
    Task<bool> RemoveCollaboratorAsync(string collabId);
}

public class CollaboratorDataService : ICollaboratorDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<CollaboratorDataService> _logger;

    public CollaboratorDataService(IDbConnectionFactory connectionFactory, ILogger<CollaboratorDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<CollaboratorDto>> GetCollaboratorsByTripAsync(string tripId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT tc.collab_id AS CollabId, tc.trip_id AS TripId, tc.user_id AS UserId,
                       tc.person_id AS PersonId, p.name AS PersonName, tc.phone_number AS PhoneNumber,
                       u.full_name AS FullName, tc.role AS Role, tc.status AS Status, tc.joined_at AS JoinedAt
                FROM TripCollaborators tc
                LEFT JOIN Users u ON tc.user_id = u.user_id
                LEFT JOIN Persons p ON tc.person_id = p.person_id
                WHERE tc.trip_id = @TripId
                ORDER BY tc.joined_at ASC";

            return await conn.QueryAsync<CollaboratorDto>(sql, new { TripId = tripId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collaborators for trip: {TripId}", tripId);
            throw;
        }
    }

    public async Task<CollaboratorDto?> GetCollaboratorByIdAsync(string collabId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT tc.collab_id AS CollabId, tc.trip_id AS TripId, tc.user_id AS UserId,
                       tc.person_id AS PersonId, p.name AS PersonName, tc.phone_number AS PhoneNumber,
                       u.full_name AS FullName, tc.role AS Role, tc.status AS Status, tc.joined_at AS JoinedAt
                FROM TripCollaborators tc
                LEFT JOIN Users u ON tc.user_id = u.user_id
                LEFT JOIN Persons p ON tc.person_id = p.person_id
                WHERE tc.collab_id = @CollabId";

            return await conn.QuerySingleOrDefaultAsync<CollaboratorDto>(sql, new { CollabId = collabId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collaborator by ID: {CollabId}", collabId);
            throw;
        }
    }

    public async Task<CollaboratorDto> InviteCollaboratorAsync(InviteCollaboratorDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var collabId = Guid.NewGuid().ToString();
            var role = !string.IsNullOrWhiteSpace(dto.Role) ? dto.Role.ToUpper() : "MEMBER";

            const string sql = @"
                INSERT INTO TripCollaborators (collab_id, trip_id, user_id, person_id, phone_number, role, status, joined_at)
                VALUES (@CollabId, @TripId, @UserId, @PersonId, @PhoneNumber, @Role, 'ACCEPTED', GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                CollabId = collabId,
                dto.TripId,
                dto.UserId,
                dto.PersonId,
                dto.PhoneNumber,
                Role = role
            });

            _logger.LogInformation("Collaborator {CollabId} added to trip {TripId}", collabId, dto.TripId);

            return new CollaboratorDto
            {
                CollabId = collabId,
                TripId = dto.TripId,
                UserId = dto.UserId,
                PersonId = dto.PersonId,
                PhoneNumber = dto.PhoneNumber,
                Role = role,
                Status = "ACCEPTED",
                JoinedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inviting collaborator to trip: {TripId}", dto.TripId);
            throw;
        }
    }

    public async Task<bool> UpdateCollaboratorRoleAsync(string collabId, string role)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var rows = await conn.ExecuteAsync(
                "UPDATE TripCollaborators SET role = @Role WHERE collab_id = @CollabId",
                new { CollabId = collabId, Role = role.ToUpper() });

            _logger.LogInformation("Collaborator {CollabId} role updated to {Role}", collabId, role);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating collaborator role: {CollabId}", collabId);
            throw;
        }
    }

    public async Task<bool> RemoveCollaboratorAsync(string collabId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var rows = await conn.ExecuteAsync(
                "DELETE FROM TripCollaborators WHERE collab_id = @CollabId",
                new { CollabId = collabId });

            _logger.LogInformation("Collaborator {CollabId} removed", collabId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing collaborator: {CollabId}", collabId);
            throw;
        }
    }
}
