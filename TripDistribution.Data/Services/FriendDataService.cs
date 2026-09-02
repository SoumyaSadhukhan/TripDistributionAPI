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
    Task<IEnumerable<FriendDto>> GetFriendsAsync(string userId, string? userPhone);
    Task<FriendDto> AddOrInviteFriendAsync(string currentUserId, string? currentUserPhone, FriendInviteDto dto);
    Task<bool> UpdateFriendStatusAsync(string connectionId, string status);
    Task<bool> DeleteFriendAsync(string connectionId);
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

    public async Task<IEnumerable<FriendDto>> GetFriendsAsync(string userId, string? userPhone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var cleanPhone = !string.IsNullOrWhiteSpace(userPhone) ? userPhone.Replace(" ", "").Trim() : "";
            var last10 = cleanPhone.Length >= 10 ? cleanPhone[^10..] : cleanPhone;

            const string sql = @"
                SELECT connection_id AS ConnectionId, user_id AS UserId, friend_user_id AS FriendUserId,
                       friend_phone AS FriendPhone, friend_name AS FriendName,
                       diet_type AS DietType, diet_name AS DietName, status AS Status,
                       requester_id AS RequesterId, receiver_id AS ReceiverId,
                       CASE WHEN receiver_id = @UserId OR (@Last10 <> '' AND RIGHT(friend_phone, 10) = @Last10 AND user_id <> @UserId) THEN 1 ELSE 0 END AS IsIncoming,
                       created_at AS CreatedAt
                FROM FriendConnections
                WHERE user_id = @UserId 
                   OR friend_user_id = @UserId 
                   OR receiver_id = @UserId
                   OR (@Last10 <> '' AND RIGHT(friend_phone, 10) = @Last10)
                ORDER BY created_at DESC";

            return await conn.QueryAsync<FriendDto>(sql, new { UserId = userId, Last10 = last10 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting friends for user: {UserId}", userId);
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

            var registeredUser = await conn.QuerySingleOrDefaultAsync<User>(
                "SELECT user_id, phone_number, full_name, diet_type, diet_name FROM Users WHERE RIGHT(phone_number, 10) = @Last10 AND is_active = 1",
                new { Last10 = last10 });

            var connectionId = Guid.NewGuid().ToString();
            var dietName = registeredUser?.DietName ?? (!string.IsNullOrWhiteSpace(dto.DietName) ? dto.DietName : dto.DietType.ToString());
            var dietType = registeredUser != null ? registeredUser.DietType : dto.DietType;
            var friendName = registeredUser?.FullName ?? dto.FriendName;

            const string sql = @"
                INSERT INTO FriendConnections (connection_id, user_id, friend_user_id, friend_phone, friend_name, diet_type, diet_name, status, requester_id, receiver_id, created_at)
                VALUES (@ConnectionId, @UserId, @FriendUserId, @FriendPhone, @FriendName, @DietType, @DietName, 'ACCEPTED', @RequesterId, @ReceiverId, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                ConnectionId = connectionId,
                UserId = currentUserId,
                FriendUserId = registeredUser?.UserId,
                FriendPhone = cleanPhone,
                FriendName = friendName,
                DietType = (int)dietType,
                DietName = dietName,
                RequesterId = currentUserId,
                ReceiverId = registeredUser?.UserId
            });

            _logger.LogInformation("Friend connection created between {UserId} and phone {FriendPhone}", currentUserId, cleanPhone);

            return new FriendDto
            {
                ConnectionId = connectionId,
                UserId = currentUserId,
                FriendUserId = registeredUser?.UserId,
                FriendPhone = cleanPhone,
                FriendName = friendName,
                DietType = dietType,
                DietName = dietName,
                Status = "ACCEPTED",
                RequesterId = currentUserId,
                ReceiverId = registeredUser?.UserId,
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

    public async Task<bool> UpdateFriendStatusAsync(string connectionId, string status)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var rows = await conn.ExecuteAsync(
                "UPDATE FriendConnections SET status = @Status WHERE connection_id = @ConnectionId",
                new { ConnectionId = connectionId, Status = status.ToUpper() });

            _logger.LogInformation("Friend connection {ConnectionId} status updated to {Status}", connectionId, status);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating friend connection status: {ConnectionId}", connectionId);
            throw;
        }
    }

    public async Task<bool> DeleteFriendAsync(string connectionId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var rows = await conn.ExecuteAsync(
                "DELETE FROM FriendConnections WHERE connection_id = @ConnectionId",
                new { ConnectionId = connectionId });

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
