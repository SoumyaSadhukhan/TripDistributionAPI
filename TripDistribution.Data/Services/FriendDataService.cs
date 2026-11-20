using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Friends;
using TripDistribution.Model.Entities;
using TripDistribution.Model.Enums;

namespace TripDistribution.Data.Services;

public interface IFriendDataService
{
    Task<IEnumerable<ContactMatchResultDto>> CheckContactsAsync(List<string> contacts);
    Task<IEnumerable<ContactMatchResultDto>> SearchUsersAsync(string query);
    Task<IEnumerable<FriendDto>> GetFriendsAsync(string userId, string? userPhone);
    Task<IEnumerable<FriendDto>> GetFriendRequestsAsync(string userId);
    Task<FriendDto> AddOrInviteFriendAsync(string currentUserId, string? currentUserPhone, FriendInviteDto dto);
    Task<bool> UpdateFriendStatusAsync(string connectionId, string userId, string status);
    Task<bool> DeleteFriendAsync(string connectionId, string userId);
}

public class FriendDataService : IFriendDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FriendDataService> _logger;

    public FriendDataService(IDbConnectionFactory connectionFactory, ILogger<FriendDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<ContactMatchResultDto>> CheckContactsAsync(List<string> contacts)
    {
        try
        {
            if (contacts.Count == 0) return [];

            var cleanPhones = contacts
                .Select(c => new string(c.Where(char.IsDigit).ToArray()))
                .Where(c => c.Length >= 7)
                .Select(c => c.Length >= 10 ? c[^10..] : c)
                .Distinct()
                .ToList();

            if (cleanPhones.Count == 0) return [];

            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT user_id AS UserId, phone_number AS Phone, full_name AS FullName,
                       diet_type AS DietType, diet_name AS DietName, 1 AS IsRegistered
                FROM Users
                WHERE RIGHT(phone_number, 10) IN @Phones AND is_active = 1";

            return await conn.QueryAsync<ContactMatchResultDto>(sql, new { Phones = cleanPhones });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking contacts batch");
            throw;
        }
    }

    public async Task<IEnumerable<ContactMatchResultDto>> SearchUsersAsync(string query)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var clean = !string.IsNullOrWhiteSpace(query) ? query.Replace(" ", "").Trim() : "";
            var last10 = clean.Length >= 10 ? clean[^10..] : clean;

            const string sql = @"
                SELECT user_id AS UserId, phone_number AS Phone, full_name AS FullName,
                       diet_type AS DietType, diet_name AS DietName, 1 AS IsRegistered
                FROM Users
                WHERE is_active = 1 
                  AND (
                      (@Last10 <> '' AND RIGHT(phone_number, 10) = @Last10) 
                      OR full_name LIKE '%' + @Query + '%'
                  )";

            return await conn.QueryAsync<ContactMatchResultDto>(sql, new { Last10 = last10, Query = query.Trim() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users for query: {Query}", query);
            throw;
        }
    }

    public async Task<IEnumerable<FriendDto>> GetFriendsAsync(string userId, string? userPhone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = !string.IsNullOrWhiteSpace(userPhone) ? userPhone.Replace(" ", "").Trim() : "";
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            const string sql = @"
                SELECT 
                    fc.connection_id AS ConnectionId, 
                    @UserId AS UserId, 
                    CASE WHEN fc.user_id = @UserId THEN fc.friend_user_id ELSE fc.user_id END AS FriendUserId,
                    COALESCE(CASE WHEN fc.user_id = @UserId THEN u2.phone_number ELSE u1.phone_number END, fc.friend_phone, '') AS FriendPhone,
                    COALESCE(CASE WHEN fc.user_id = @UserId THEN u2.full_name ELSE u1.full_name END,
                             CASE WHEN fc.user_id = @UserId THEN u2.username ELSE u1.username END,
                             fc.friend_name,
                             'Friend') AS FriendName,
                    COALESCE(CASE WHEN fc.user_id = @UserId THEN u2.diet_type ELSE u1.diet_type END, fc.diet_type) AS DietType,
                    COALESCE(CASE WHEN fc.user_id = @UserId THEN u2.diet_name ELSE u1.diet_name END, fc.diet_name, 'Vegetarian') AS DietName,
                    fc.status AS Status,
                    fc.requester_id AS RequesterId, 
                    fc.receiver_id AS ReceiverId,
                    CASE WHEN fc.receiver_id = @UserId OR (@Last10 <> '' AND RIGHT(fc.friend_phone, 10) = @Last10 AND fc.user_id <> @UserId) THEN 1 ELSE 0 END AS IsIncoming,
                    fc.created_at AS CreatedAt
                FROM FriendConnections fc
                LEFT JOIN Users u1 ON u1.user_id = fc.user_id
                LEFT JOIN Users u2 ON (u2.user_id = fc.friend_user_id OR (u2.phone_number IS NOT NULL AND fc.friend_phone IS NOT NULL AND RIGHT(u2.phone_number, 10) = RIGHT(fc.friend_phone, 10)))
                WHERE (fc.user_id = @UserId 
                   OR fc.friend_user_id = @UserId 
                   OR fc.receiver_id = @UserId
                   OR (@Last10 <> '' AND RIGHT(fc.friend_phone, 10) = @Last10))
                  AND fc.status = 'ACCEPTED'
                ORDER BY fc.created_at DESC";

            return await conn.QueryAsync<FriendDto>(sql, new { UserId = userId, Last10 = last10 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting friends for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<FriendDto>> GetFriendRequestsAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT 
                    fc.connection_id AS ConnectionId, 
                    @UserId AS UserId, 
                    fc.user_id AS FriendUserId,
                    COALESCE(u_req.phone_number, fc.friend_phone, '') AS FriendPhone,
                    COALESCE(u_req.full_name, u_req.username, fc.friend_name, 'Member') AS FriendName,
                    COALESCE(u_req.diet_type, fc.diet_type) AS DietType,
                    COALESCE(u_req.diet_name, fc.diet_name, 'Vegetarian') AS DietName,
                    fc.status AS Status,
                    fc.requester_id AS RequesterId, 
                    fc.receiver_id AS ReceiverId,
                    COALESCE(u_req.full_name, u_req.username, 'Member') AS RequesterName,
                    COALESCE(u_req.phone_number, '') AS RequesterPhone,
                    1 AS IsIncoming, 
                    fc.created_at AS CreatedAt
                FROM FriendConnections fc
                LEFT JOIN Users u_req ON u_req.user_id = COALESCE(fc.requester_id, fc.user_id)
                WHERE (fc.receiver_id = @UserId OR (fc.friend_user_id = @UserId AND (fc.requester_id IS NULL OR fc.requester_id <> @UserId)))
                  AND fc.status = 'PENDING'
                ORDER BY fc.created_at DESC";

            return await conn.QueryAsync<FriendDto>(sql, new { UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting friend requests for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<FriendDto> AddOrInviteFriendAsync(string currentUserId, string? currentUserPhone, FriendInviteDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = dto.FriendPhone.Replace(" ", "").Trim();
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            var curCleanPhone = !string.IsNullOrWhiteSpace(currentUserPhone) ? currentUserPhone.Replace(" ", "").Trim() : "";
            var curLast10 = curCleanPhone.Length >= 10 ? curCleanPhone[^10..] : curCleanPhone;

            var registeredUser = await conn.QuerySingleOrDefaultAsync<User>(
                "SELECT user_id AS UserId, phone_number AS PhoneNumber, full_name AS FullName, diet_type AS DietType, diet_name AS DietName FROM Users WHERE RIGHT(phone_number, 10) = @Last10 AND is_active = 1",
                new { Last10 = last10 });

            // Self-invitation prevention
            if (registeredUser != null && registeredUser.UserId == currentUserId)
            {
                throw new InvalidOperationException("You cannot send a friend request to yourself.");
            }
            if (!string.IsNullOrWhiteSpace(curLast10) && last10 == curLast10)
            {
                throw new InvalidOperationException("You cannot send a friend request to yourself.");
            }

            var targetUserId = registeredUser?.UserId;
            var dietName = registeredUser?.DietName ?? (!string.IsNullOrWhiteSpace(dto.DietName) ? dto.DietName : dto.DietType.ToString());
            var dietType = registeredUser != null ? registeredUser.DietType : dto.DietType;
            var friendName = registeredUser?.FullName ?? dto.FriendName;

            // Check for existing friendship / pending request
            const string existingSql = @"
                SELECT TOP 1 connection_id AS ConnectionId, user_id AS UserId, friend_user_id AS FriendUserId,
                       friend_phone AS FriendPhone, friend_name AS FriendName,
                       diet_type AS DietType, diet_name AS DietName, status AS Status,
                       requester_id AS RequesterId, receiver_id AS ReceiverId, created_at AS CreatedAt
                FROM FriendConnections
                WHERE (
                    (user_id = @CurrentUserId AND (friend_user_id = @TargetUserId OR (@Last10 <> '' AND RIGHT(friend_phone, 10) = @Last10)))
                    OR (@TargetUserId IS NOT NULL AND user_id = @TargetUserId AND (friend_user_id = @CurrentUserId OR (@CurLast10 <> '' AND RIGHT(friend_phone, 10) = @CurLast10)))
                    OR (receiver_id = @CurrentUserId AND (requester_id = @TargetUserId OR (@Last10 <> '' AND RIGHT(friend_phone, 10) = @Last10)))
                    OR (requester_id = @CurrentUserId AND (receiver_id = @TargetUserId OR (@Last10 <> '' AND RIGHT(friend_phone, 10) = @Last10)))
                )
                ORDER BY created_at DESC";

            var existing = await conn.QueryFirstOrDefaultAsync<FriendDto>(existingSql, new
            {
                CurrentUserId = currentUserId,
                TargetUserId = targetUserId,
                Last10 = last10,
                CurLast10 = curLast10
            });

            if (existing != null)
            {
                if (existing.Status == "ACCEPTED")
                {
                    _logger.LogInformation("Friendship already exists between {User1} and {User2}", currentUserId, targetUserId ?? cleanPhone);
                    return existing;
                }

                if (existing.Status == "PENDING")
                {
                    var isExistingSender = existing.RequesterId == currentUserId || (existing.RequesterId == null && existing.UserId == currentUserId);
                    if (isExistingSender)
                    {
                        // Duplicate request from same sender
                        _logger.LogInformation("Pending friend request already exists from {UserId} to {Target}", currentUserId, targetUserId ?? cleanPhone);
                        return existing;
                    }
                    else
                    {
                        // Reciprocal request! Auto-accept safely
                        await conn.ExecuteAsync("UPDATE FriendConnections SET status = 'ACCEPTED' WHERE connection_id = @ConnectionId", new { ConnectionId = existing.ConnectionId });
                        existing.Status = "ACCEPTED";
                        _logger.LogInformation("Reciprocal request detected: Auto-accepted connection {ConnectionId}", existing.ConnectionId);
                        return existing;
                    }
                }

                // If REJECTED or CANCELLED, re-open to PENDING
                if (existing.Status == "REJECTED" || existing.Status == "CANCELLED")
                {
                    await conn.ExecuteAsync(
                        @"UPDATE FriendConnections 
                          SET status = 'PENDING', requester_id = @CurrentUserId, receiver_id = @TargetUserId,
                              friend_name = @FriendName, diet_type = @DietType, diet_name = @DietName, created_at = GETDATE()
                          WHERE connection_id = @ConnectionId",
                        new
                        {
                            ConnectionId = existing.ConnectionId,
                            CurrentUserId = currentUserId,
                            TargetUserId = targetUserId,
                            FriendName = friendName,
                            DietType = (int)dietType,
                            DietName = dietName
                        });

                    existing.Status = "PENDING";
                    existing.RequesterId = currentUserId;
                    existing.ReceiverId = targetUserId;
                    return existing;
                }
            }

            // Fresh Request: Create PENDING friend request
            var connectionId = Guid.NewGuid().ToString();
            const string sql = @"
                INSERT INTO FriendConnections (connection_id, user_id, friend_user_id, friend_phone, friend_name, diet_type, diet_name, status, requester_id, receiver_id, created_at)
                VALUES (@ConnectionId, @UserId, @FriendUserId, @FriendPhone, @FriendName, @DietType, @DietName, 'PENDING', @RequesterId, @ReceiverId, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                ConnectionId = connectionId,
                UserId = currentUserId,
                FriendUserId = targetUserId,
                FriendPhone = cleanPhone,
                FriendName = friendName,
                DietType = (int)dietType,
                DietName = dietName,
                RequesterId = currentUserId,
                ReceiverId = targetUserId
            });

            _logger.LogInformation("Friend request sent from {UserId} to phone {FriendPhone} (status: PENDING)", currentUserId, cleanPhone);

            return new FriendDto
            {
                ConnectionId = connectionId,
                UserId = currentUserId,
                FriendUserId = targetUserId,
                FriendPhone = cleanPhone,
                FriendName = friendName,
                DietType = dietType,
                DietName = dietName,
                Status = "PENDING",
                RequesterId = currentUserId,
                ReceiverId = targetUserId,
                IsIncoming = false,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding friend: {FriendPhone}", dto.FriendPhone);
            throw;
        }
    }

    public async Task<bool> UpdateFriendStatusAsync(string connectionId, string userId, string status)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string selectSql = @"
                SELECT connection_id AS ConnectionId, user_id AS UserId, friend_user_id AS FriendUserId,
                       status AS Status, requester_id AS RequesterId, receiver_id AS ReceiverId
                FROM FriendConnections
                WHERE connection_id = @ConnectionId";

            var record = await conn.QuerySingleOrDefaultAsync<dynamic>(selectSql, new { ConnectionId = connectionId });
            if (record == null)
            {
                _logger.LogWarning("Friend connection {ConnectionId} not found", connectionId);
                return false;
            }

            string currentStatus = ((string)record.Status).ToUpperInvariant();
            string? requesterId = (string?)record.RequesterId ?? (string?)record.UserId;
            string? receiverId = (string?)record.ReceiverId ?? (string?)record.FriendUserId;

            var newStatus = status.Trim().ToUpperInvariant();

            // Idempotent: If connection is already in requested state, return success immediately
            if (currentStatus == newStatus)
            {
                _logger.LogInformation("Friend connection {ConnectionId} is already in state {Status} - idempotent success", connectionId, newStatus);
                return true;
            }

            // Validate state transitions: PENDING -> ACCEPTED, PENDING -> REJECTED, PENDING -> CANCELLED
            if (currentStatus == "PENDING")
            {
                if (newStatus == "ACCEPTED" || newStatus == "REJECTED")
                {
                    // Only recipient can accept or reject
                    if (userId != receiverId)
                    {
                        _logger.LogWarning("Unauthorized: User {UserId} cannot accept/reject request intended for {ReceiverId}", userId, receiverId);
                        return false;
                    }
                }
                else if (newStatus == "CANCELLED")
                {
                    // Only requester/sender can cancel
                    if (userId != requesterId)
                    {
                        _logger.LogWarning("Unauthorized: User {UserId} cannot cancel request initiated by {RequesterId}", userId, requesterId);
                        return false;
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid transition from PENDING to {NewStatus}", newStatus);
                    return false;
                }
            }
            else
            {
                // Cannot transition non-PENDING status (e.g. ACCEPTED -> REJECTED or REJECTED -> ACCEPTED directly)
                _logger.LogWarning("Invalid transition: Cannot change connection {ConnectionId} from {CurrentStatus} to {NewStatus}", connectionId, currentStatus, newStatus);
                return false;
            }

            var rows = await conn.ExecuteAsync(
                "UPDATE FriendConnections SET status = @Status WHERE connection_id = @ConnectionId",
                new { ConnectionId = connectionId, Status = newStatus });

            _logger.LogInformation("Friend connection {ConnectionId} status transitioned: {CurrentStatus} -> {Status}", connectionId, currentStatus, newStatus);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating friend connection status: {ConnectionId}", connectionId);
            throw;
        }
    }

    public async Task<bool> DeleteFriendAsync(string connectionId, string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var rows = await conn.ExecuteAsync(
                @"DELETE FROM FriendConnections 
                  WHERE connection_id = @ConnectionId 
                    AND (user_id = @UserId OR friend_user_id = @UserId OR receiver_id = @UserId OR requester_id = @UserId)",
                new { ConnectionId = connectionId, UserId = userId });

            _logger.LogInformation("Friend connection {ConnectionId} deleted", connectionId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting friend connection: {ConnectionId}", connectionId);
            throw;
        }
    }
}
