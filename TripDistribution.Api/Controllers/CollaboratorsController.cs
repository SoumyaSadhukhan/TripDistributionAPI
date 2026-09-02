using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Collaborators;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CollaboratorsController : ControllerBase
{
    private readonly ICollaboratorDataService _collaboratorDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly INotificationDataService _notificationDataService;
    private readonly ILogger<CollaboratorsController> _logger;

    public CollaboratorsController(
        ICollaboratorDataService collaboratorDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        INotificationDataService notificationDataService,
        ILogger<CollaboratorsController> logger)
    {
        _collaboratorDataService = collaboratorDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _notificationDataService = notificationDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetCollaborators(string tripId)
    {
        try
        {
            var collabs = await _collaboratorDataService.GetCollaboratorsByTripAsync(tripId);
            return Ok(ApiResponse<IEnumerable<CollaboratorDto>>.Ok(collabs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetCollaborators for trip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch collaborators."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> InviteCollaborator([FromBody] InviteCollaboratorDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.TripId))
                return BadRequest(ApiResponse.Fail("TripId is required."));

            var collab = await _collaboratorDataService.InviteCollaboratorAsync(dto);
            await _activityLogDataService.LogActivityAsync(_currentUserContext.UserId, dto.TripId, "INVITE_COLLABORATOR", $"Invited collaborator {dto.PhoneNumber ?? dto.UserId}");
            _logger.LogInformation("Collaborator invited to trip {TripId}", dto.TripId);

            if (!string.IsNullOrWhiteSpace(dto.UserId))
            {
                await _notificationDataService.CreateNotificationAsync(new Notification
                {
                    UserId = dto.UserId,
                    TripId = dto.TripId,
                    Title = "Trip Collaboration Invite",
                    Message = "You were added as a collaborator to a trip.",
                    Type = "COLLAB_INVITE"
                });
            }

            return Ok(ApiResponse<CollaboratorDto>.Ok(collab, "Collaborator added successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in InviteCollaborator for trip: {TripId}", dto.TripId);
            return StatusCode(500, ApiResponse.Fail("Failed to invite collaborator."));
        }
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateCollaboratorRoleDto dto)
    {
        try
        {
            var updated = await _collaboratorDataService.UpdateCollaboratorRoleAsync(id, dto.Role);
            if (!updated)
                return NotFound(ApiResponse.Fail("Collaborator not found."));

            _logger.LogInformation("Collaborator {CollabId} role updated to {Role}", id, dto.Role);
            return Ok(ApiResponse.Ok("Role updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateRole for collaborator: {CollabId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update role."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveCollaborator(string id)
    {
        try
        {
            var removed = await _collaboratorDataService.RemoveCollaboratorAsync(id);
            if (!removed)
                return NotFound(ApiResponse.Fail("Collaborator not found."));

            _logger.LogInformation("Collaborator {CollabId} removed", id);
            return Ok(ApiResponse.Ok("Collaborator removed successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in RemoveCollaborator: {CollabId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to remove collaborator."));
        }
    }
}
