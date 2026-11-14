using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Groups;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly IGroupDataService _groupDataService;
    private readonly ITripDataService _tripDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly ILogger<GroupsController> _logger;

    public GroupsController(
        IGroupDataService groupDataService,
        ITripDataService tripDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        ILogger<GroupsController> logger)
    {
        _groupDataService = groupDataService;
        _tripDataService = tripDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetGroupsByTrip(string tripId)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var hasAccess = await _tripDataService.HasUserTripAccessAsync(tripId, userId);
            if (!hasAccess)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to view groups for this trip."));

            var groups = await _groupDataService.GetGroupsByTripAsync(tripId);
            return Ok(ApiResponse<IEnumerable<GroupDto>>.Ok(groups));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetGroupsByTrip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch groups."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.TripId) || string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(ApiResponse.Fail("TripId and Name are required."));

            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var canManage = await _tripDataService.CanUserManageTripAsync(dto.TripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to create groups for this trip."));

            var group = await _groupDataService.CreateGroupAsync(dto);
            await _activityLogDataService.LogActivityAsync(userId, dto.TripId, "CREATE_GROUP", $"Created group '{dto.Name}'");
            _logger.LogInformation("Group {GroupId} '{Name}' created in trip {TripId}", group.GroupId, dto.Name, dto.TripId);

            return Ok(ApiResponse<Group>.Ok(group, "Group created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CreateGroup for trip: {TripId}", dto.TripId);
            return StatusCode(500, ApiResponse.Fail("Failed to create group."));
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateGroup(string id, [FromBody] UpdateGroupDto dto)
    {
        try
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(ApiResponse.Fail("Group name is required."));

            var name = dto.Name;

            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var existing = await _groupDataService.GetGroupByIdAsync(id);
            if (existing == null)
                return NotFound(ApiResponse.Fail("Group not found."));

            var canManage = await _tripDataService.CanUserManageTripAsync(existing.TripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to modify groups for this trip."));

            var updated = await _groupDataService.UpdateGroupAsync(id, name);
            if (!updated)
                return NotFound(ApiResponse.Fail("Group not found."));

            _logger.LogInformation("Group {GroupId} updated to '{Name}'", id, name);
            return Ok(ApiResponse.Ok("Group updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateGroup: {GroupId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update group."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var existing = await _groupDataService.GetGroupByIdAsync(id);
            if (existing == null)
                return NotFound(ApiResponse.Fail("Group not found."));

            var canManage = await _tripDataService.CanUserManageTripAsync(existing.TripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to delete groups for this trip."));

            var deleted = await _groupDataService.DeleteGroupAsync(id);
            if (!deleted)
                return NotFound(ApiResponse.Fail("Group not found."));

            _logger.LogInformation("Group {GroupId} deleted", id);
            return Ok(ApiResponse.Ok("Group deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeleteGroup: {GroupId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to delete group."));
        }
    }
}
