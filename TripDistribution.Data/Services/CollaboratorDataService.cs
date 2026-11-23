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
    Task<bool> AddOrUpdatePendingCollaboratorAsync(string tripId, string? userId, string? personId, string? phone);
    Task<bool> AcceptTripInviteAsync(string tripId, string userId, string? phone);
    Task<bool> DeclineTripInviteAsync(string tripId, string userId, string? phone);
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
            var status = !string.IsNullOrWhiteSpace(dto.UserId) ? "PENDING" : "ACCEPTED";

            const string sql = @"
                INSERT INTO TripCollaborators (collab_id, trip_id, user_id, person_id, phone_number, role, status, joined_at)
                VALUES (@CollabId, @TripId, @UserId, @PersonId, @PhoneNumber, @Role, @Status, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                CollabId = collabId,
                dto.TripId,
                dto.UserId,
                dto.PersonId,
                dto.PhoneNumber,
                Role = role,
                Status = status
            });

            _logger.LogInformation("Collaborator {CollabId} added to trip {TripId} with status {Status}", collabId, dto.TripId, status);

            return new CollaboratorDto
            {
                CollabId = collabId,
                TripId = dto.TripId,
                UserId = dto.UserId,
                PersonId = dto.PersonId,
                PhoneNumber = dto.PhoneNumber,
                Role = role,
                Status = status,
                JoinedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inviting collaborator to trip: {TripId}", dto.TripId);
            throw;
        }
    }

    public async Task<bool> AddOrUpdatePendingCollaboratorAsync(string tripId, string? userId, string? personId, string? phone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                IF EXISTS (
                    SELECT 1 FROM TripCollaborators 
                    WHERE trip_id = @TripId 
                      AND ((@UserId IS NOT NULL AND user_id = @UserId) OR (@PersonId IS NOT NULL AND person_id = @PersonId))
                )
                BEGIN
                    UPDATE TripCollaborators
                    SET person_id = COALESCE(@PersonId, person_id),
                        phone_number = COALESCE(@PhoneNumber, phone_number),
                        user_id = COALESCE(@UserId, user_id)
                    WHERE trip_id = @TripId 
                      AND ((@UserId IS NOT NULL AND user_id = @UserId) OR (@PersonId IS NOT NULL AND person_id = @PersonId));
                END
                ELSE
                BEGIN
                    INSERT INTO TripCollaborators (collab_id, trip_id, user_id, person_id, phone_number, role, status, joined_at)
                    VALUES (NEWID(), @TripId, @UserId, @PersonId, @PhoneNumber, 'MEMBER', 'PENDING', GETDATE());
                END";

            await conn.ExecuteAsync(sql, new
            {
                TripId = tripId,
                UserId = userId,
                PersonId = personId,
                PhoneNumber = phone
            });
            _logger.LogInformation("Pending collaborator registered for trip {TripId}, user {UserId}", tripId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/updating pending collaborator for trip: {TripId}, user: {UserId}", tripId, userId);
            return false;
        }
    }

    public async Task<bool> AcceptTripInviteAsync(string tripId, string userId, string? phone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = !string.IsNullOrWhiteSpace(phone) ? phone.Replace(" ", "").Trim() : "";
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            const string sql = @"
                UPDATE TripCollaborators
                SET status = 'ACCEPTED', user_id = @UserId, joined_at = GETDATE()
                WHERE trip_id = @TripId 
                  AND (user_id = @UserId OR (@Last10 <> '' AND RIGHT(phone_number, 10) = @Last10));

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO TripCollaborators (collab_id, trip_id, user_id, phone_number, role, status, joined_at)
                    SELECT NEWID(), @TripId, @UserId, ISNULL(@Phone, phone_number), 'MEMBER', 'ACCEPTED', GETDATE()
                    FROM Persons
                    WHERE trip_id = @TripId AND (user_id = @UserId OR (@Last10 <> '' AND RIGHT(phone_number, 10) = @Last10));
                END

                UPDATE Persons
                SET user_id = @UserId
                WHERE trip_id = @TripId 
                  AND (user_id IS NULL OR user_id = '') 
                  AND @Last10 <> '' AND RIGHT(phone_number, 10) = @Last10;

                UPDATE Notifications
                SET is_read = 1,
                    type = 'TRIP_INVITE_ACCEPTED',
                    title = 'Trip Request Accepted',
                    message = 'You accepted the trip request' + CASE WHEN trip_name IS NOT NULL AND trip_name <> '' THEN ' to join ""' + trip_name + '""' ELSE '' END
                WHERE user_id = @UserId AND trip_id = @TripId AND type IN ('TRIP_INVITE', 'COLLAB_INVITE', 'TRIP_INVITE_ACCEPTED');";

            await conn.ExecuteAsync(sql, new { TripId = tripId, UserId = userId, Phone = phone, Last10 = last10 });
            _logger.LogInformation("Trip invite accepted for trip {TripId} by user {UserId}", tripId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting trip invite for trip: {TripId}, user: {UserId}", tripId, userId);
            return false;
        }
    }

    public async Task<bool> DeclineTripInviteAsync(string tripId, string userId, string? phone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = !string.IsNullOrWhiteSpace(phone) ? phone.Replace(" ", "").Trim() : "";
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            const string sql = @"
                UPDATE TripCollaborators
                SET status = 'REJECTED'
                WHERE trip_id = @TripId 
                  AND (user_id = @UserId OR (@Last10 <> '' AND RIGHT(phone_number, 10) = @Last10));

                UPDATE Notifications
                SET is_read = 1,
                    type = 'TRIP_INVITE_DECLINED',
                    title = 'Trip Request Declined',
                    message = 'You declined the trip request' + CASE WHEN trip_name IS NOT NULL AND trip_name <> '' THEN ' to join ""' + trip_name + '""' ELSE '' END
                WHERE user_id = @UserId AND trip_id = @TripId AND type IN ('TRIP_INVITE', 'COLLAB_INVITE', 'TRIP_INVITE_DECLINED');";

            await conn.ExecuteAsync(sql, new { TripId = tripId, UserId = userId, Last10 = last10 });
            _logger.LogInformation("Trip invite declined for trip {TripId} by user {UserId}", tripId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error declining trip invite for trip: {TripId}, user: {UserId}", tripId, userId);
            return false;
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
