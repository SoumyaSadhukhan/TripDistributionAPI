using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Notifications;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationDataService _notificationDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationDataService notificationDataService,
        ICurrentUserContext currentUserContext,
        ILogger<NotificationsController> logger)
    {
        _notificationDataService = notificationDataService;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications()
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var notifications = await _notificationDataService.GetUserNotificationsAsync(userId);
            return Ok(ApiResponse<IEnumerable<NotificationDto>>.Ok(notifications));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetMyNotifications for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch notifications."));
        }
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var count = await _notificationDataService.GetUnreadCountAsync(userId);
            return Ok(ApiResponse<int>.Ok(count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetUnreadCount for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch unread count."));
        }
    }

    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead([FromBody] MarkNotificationReadDto dto)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            await _notificationDataService.MarkAsReadAsync(userId, dto);
            _logger.LogInformation("Notifications marked as read for user: {UserId}", userId);
            return Ok(ApiResponse.Ok("Notifications marked as read."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in MarkRead for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to mark notifications as read."));
        }
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto)
    {
        try
        {
            var senderId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(senderId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            if (string.IsNullOrWhiteSpace(dto.ToUserId) || string.IsNullOrWhiteSpace(dto.Message))
                return BadRequest(ApiResponse.Fail("ToUserId and Message are required."));

            var notif = new TripDistribution.Model.Entities.Notification
            {
                NotificationId = Guid.NewGuid().ToString(),
                UserId = dto.ToUserId,
                TripId = dto.TripId,
                Title = !string.IsNullOrWhiteSpace(dto.Title) ? dto.Title : $"Message from {_currentUserContext.FullName ?? "User"}",
                Message = dto.Message,
                Type = !string.IsNullOrWhiteSpace(dto.Type) ? dto.Type : "CHAT_MESSAGE"
            };

            await _notificationDataService.CreateNotificationAsync(notif);
            _logger.LogInformation("Message/Notification sent from {SenderId} to {RecipientId}", senderId, dto.ToUserId);

            return Ok(ApiResponse<TripDistribution.Model.Entities.Notification>.Ok(notif, "Message sent successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in SendMessage from user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to send message."));
        }
    }
}
