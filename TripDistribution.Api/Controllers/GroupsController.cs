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
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly ILogger<GroupsController> _logger;

    public GroupsController(
        IGroupDataService groupDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        ILogger<GroupsController> logger)
    {
        _groupDataService = groupDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetGroupsByTrip(string tripId)
    {
        try
        {
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

            var group = await _groupDataService.CreateGroupAsync(dto);
            await _activityLogDataService.LogActivityAsync(_currentUserContext.UserId, dto.TripId, "CREATE_GROUP", $"Created group '{dto.Name}'");
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
    public async Task<IActionResult> UpdateGroup(string id, [FromBody] string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(ApiResponse.Fail("Group name is required."));

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
