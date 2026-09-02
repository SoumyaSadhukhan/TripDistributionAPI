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
}
