using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Notifications;
using TripDistribution.Model.Entities;

namespace TripDistribution.Data.Services;

public interface INotificationDataService
{
    Task<IEnumerable<NotificationDto>> GetUserNotificationsAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
    Task<bool> MarkAsReadAsync(string userId, MarkNotificationReadDto dto);
    Task CreateNotificationAsync(Notification notification);
}

public class NotificationDataService : INotificationDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<NotificationDataService> _logger;

    public NotificationDataService(IDbConnectionFactory connectionFactory, ILogger<NotificationDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<NotificationDto>> GetUserNotificationsAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT notification_id AS NotificationId, user_id AS UserId, trip_id AS TripId,
                       trip_name AS TripName, title AS Title, message AS Message,
                       type AS Type, amount AS Amount, is_read AS IsRead, created_at AS CreatedAt
                FROM Notifications
                WHERE user_id = @UserId
                ORDER BY created_at DESC";

            return await conn.QueryAsync<NotificationDto>(sql, new { UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notifications for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = "SELECT COUNT(1) FROM Notifications WHERE user_id = @UserId AND is_read = 0";
            return await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unread count for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> MarkAsReadAsync(string userId, MarkNotificationReadDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            if (dto.MarkAllAsRead)
            {
                var rows = await conn.ExecuteAsync("UPDATE Notifications SET is_read = 1 WHERE user_id = @UserId", new { UserId = userId });
                _logger.LogInformation("Marked all notifications as read for user: {UserId}", userId);
                return rows > 0;
            }
            else if (!string.IsNullOrWhiteSpace(dto.NotificationId))
            {
                var rows = await conn.ExecuteAsync(
                    "UPDATE Notifications SET is_read = 1 WHERE notification_id = @NotificationId AND user_id = @UserId",
                    new { dto.NotificationId, UserId = userId });
                _logger.LogInformation("Marked notification {NotificationId} as read for user: {UserId}", dto.NotificationId, userId);
                return rows > 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notifications as read for user: {UserId}", userId);
            throw;
        }
    }

    public async Task CreateNotificationAsync(Notification notification)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Notifications (notification_id, user_id, trip_id, trip_name, title, message, type, amount, is_read, created_at)
                VALUES (@NotificationId, @UserId, @TripId, @TripName, @Title, @Message, @Type, @Amount, 0, GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                NotificationId = string.IsNullOrWhiteSpace(notification.NotificationId) ? Guid.NewGuid().ToString() : notification.NotificationId,
                notification.UserId,
                notification.TripId,
                notification.TripName,
                notification.Title,
                notification.Message,
                notification.Type,
                notification.Amount
            });

            _logger.LogInformation("Notification created for user: {UserId}, Type: {Type}", notification.UserId, notification.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating notification for user: {UserId}", notification.UserId);
            throw;
        }
    }
}
