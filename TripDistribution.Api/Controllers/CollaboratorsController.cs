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
    private readonly ITripDataService _tripDataService;
    private readonly IFriendDataService _friendDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly INotificationDataService _notificationDataService;
    private readonly ILogger<CollaboratorsController> _logger;

    public CollaboratorsController(
        ICollaboratorDataService collaboratorDataService,
        ITripDataService tripDataService,
        IFriendDataService friendDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        INotificationDataService notificationDataService,
        ILogger<CollaboratorsController> logger)
    {
        _collaboratorDataService = collaboratorDataService;
        _tripDataService = tripDataService;
        _friendDataService = friendDataService;
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
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var hasAccess = await _tripDataService.HasUserTripAccessAsync(tripId, userId);
            if (!hasAccess)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to view collaborators for this trip."));

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

            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var canManage = await _tripDataService.CanUserManageTripAsync(dto.TripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to invite collaborators to this trip."));

            var targetUserId = dto.UserId;
            if (string.IsNullOrEmpty(targetUserId) && !string.IsNullOrWhiteSpace(dto.PhoneNumber))
            {
                try
                {
                    var matched = (await _friendDataService.SearchUsersAsync(dto.PhoneNumber)).FirstOrDefault();
                    if (matched != null && !string.IsNullOrEmpty(matched.UserId))
                    {
                        targetUserId = matched.UserId;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error resolving user for collaborator phone {Phone}", dto.PhoneNumber);
                }
            }
            dto.UserId = targetUserId;

            var collab = await _collaboratorDataService.InviteCollaboratorAsync(dto);
            await _activityLogDataService.LogActivityAsync(userId, dto.TripId, "INVITE_COLLABORATOR", $"Invited collaborator {dto.PhoneNumber ?? dto.UserId}");
            _logger.LogInformation("Collaborator invited to trip {TripId}", dto.TripId);

            if (!string.IsNullOrWhiteSpace(targetUserId) && targetUserId != userId)
            {
                var trip = await _tripDataService.GetTripDetailAsync(dto.TripId, userId);
                var inviterName = _currentUserContext.FullName ?? "A trip member";

                await _notificationDataService.CreateNotificationAsync(new Notification
                {
                    NotificationId = Guid.NewGuid().ToString(),
                    UserId = targetUserId,
                    TripId = dto.TripId,
                    TripName = trip?.Name ?? "Trip",
                    Title = "Trip Invitation",
                    Message = $"{inviterName} invited you to join trip \"{trip?.Name ?? "a trip"}\"",
                    Type = "TRIP_INVITE"
                });
            }

            return Ok(ApiResponse<CollaboratorDto>.Ok(collab, "Collaborator invited successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in InviteCollaborator for trip: {TripId}", dto.TripId);
            return StatusCode(500, ApiResponse.Fail("Failed to invite collaborator."));
        }
    }

    [HttpPost("trip/{tripId}/accept")]
    public async Task<IActionResult> AcceptTripInvite(string tripId)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            var phone = _currentUserContext.PhoneNumber;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var success = await _collaboratorDataService.AcceptTripInviteAsync(tripId, userId, phone);
            if (!success)
                return BadRequest(ApiResponse.Fail("Failed to accept trip invitation."));

            var userName = _currentUserContext.FullName ?? "User";
            await _activityLogDataService.LogActivityAsync(userId, tripId, "ACCEPT_TRIP_INVITE", $"{userName} joined the trip");
            _logger.LogInformation("User {UserId} accepted invite to trip {TripId}", userId, tripId);

            // Notify trip owner that user accepted the trip request
            try
            {
                var trip = await _tripDataService.GetTripDetailAsync(tripId, userId);
                if (trip != null && !string.IsNullOrEmpty(trip.UserId) && trip.UserId != userId)
                {
                    await _notificationDataService.CreateNotificationAsync(new Notification
                    {
                        NotificationId = Guid.NewGuid().ToString(),
                        UserId = trip.UserId,
                        TripId = tripId,
                        TripName = trip.Name,
                        Title = "Trip Request Accepted",
                        Message = $"{userName} accepted the trip request to join \"{trip.Name}\"",
                        Type = "TRIP_INVITE_ACCEPTED"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify owner of accepted trip invite for trip {TripId}", tripId);
            }

            return Ok(ApiResponse.Ok("Joined trip successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in AcceptTripInvite for trip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to accept trip invite."));
        }
    }

    [HttpPost("trip/{tripId}/decline")]
    public async Task<IActionResult> DeclineTripInvite(string tripId)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            var phone = _currentUserContext.PhoneNumber;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var success = await _collaboratorDataService.DeclineTripInviteAsync(tripId, userId, phone);
            if (!success)
                return BadRequest(ApiResponse.Fail("Failed to decline trip invitation."));

            var userName = _currentUserContext.FullName ?? "User";
            await _activityLogDataService.LogActivityAsync(userId, tripId, "DECLINE_TRIP_INVITE", $"{userName} declined the trip invitation");
            _logger.LogInformation("User {UserId} declined invite to trip {TripId}", userId, tripId);

            // Notify trip owner that user declined the trip request
            try
            {
                var trip = await _tripDataService.GetTripDetailAsync(tripId, userId);
                if (trip != null && !string.IsNullOrEmpty(trip.UserId) && trip.UserId != userId)
                {
                    await _notificationDataService.CreateNotificationAsync(new Notification
                    {
                        NotificationId = Guid.NewGuid().ToString(),
                        UserId = trip.UserId,
                        TripId = tripId,
                        TripName = trip.Name,
                        Title = "Trip Request Declined",
                        Message = $"{userName} declined the trip request to join \"{trip.Name}\"",
                        Type = "TRIP_INVITE_DECLINED"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify owner of declined trip invite for trip {TripId}", tripId);
            }

            return Ok(ApiResponse.Ok("Trip invitation declined."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeclineTripInvite for trip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to decline trip invite."));
        }
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateCollaboratorRoleDto dto)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var collab = await _collaboratorDataService.GetCollaboratorByIdAsync(id);
            if (collab == null)
                return NotFound(ApiResponse.Fail("Collaborator not found."));

            var isOwner = await _tripDataService.IsTripOwnerAsync(collab.TripId, userId);
            if (!isOwner)
                return StatusCode(403, ApiResponse.Fail("Only the trip owner can modify collaborator roles."));

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
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var collab = await _collaboratorDataService.GetCollaboratorByIdAsync(id);
            if (collab == null)
                return NotFound(ApiResponse.Fail("Collaborator not found."));

            var isOwner = await _tripDataService.IsTripOwnerAsync(collab.TripId, userId);
            var canManage = await _tripDataService.CanUserManageTripAsync(collab.TripId, userId);
            var isSelf = !string.IsNullOrEmpty(collab.UserId) && collab.UserId == userId;

            if (!isOwner && !canManage && !isSelf)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to remove this collaborator."));

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
