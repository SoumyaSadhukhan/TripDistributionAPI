using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.ActivityLogs;
using TripDistribution.Model.DTOs.Common;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ActivityLogsController : ControllerBase
{
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly ITripDataService _tripDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ActivityLogsController> _logger;

    public ActivityLogsController(
        IActivityLogDataService activityLogDataService,
        ITripDataService tripDataService,
        ICurrentUserContext currentUserContext,
        ILogger<ActivityLogsController> logger)
    {
        _activityLogDataService = activityLogDataService;
        _tripDataService = tripDataService;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetLogsByTrip(string tripId)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var hasAccess = await _tripDataService.HasUserTripAccessAsync(tripId, userId);
            if (!hasAccess)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to view activity logs for this trip."));

            var logs = await _activityLogDataService.GetActivityLogsByTripAsync(tripId);
            return Ok(ApiResponse<IEnumerable<ActivityLogDto>>.Ok(logs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetLogsByTrip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch activity logs."));
        }
    }
}
