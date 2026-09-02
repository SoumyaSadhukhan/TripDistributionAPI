using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ActivityLogsController> _logger;

    public ActivityLogsController(
        IActivityLogDataService activityLogDataService,
        ILogger<ActivityLogsController> logger)
    {
        _activityLogDataService = activityLogDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetLogsByTrip(string tripId)
    {
        try
        {
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
