using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Persons;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PersonsController : ControllerBase
{
    private readonly IPersonDataService _personDataService;
    private readonly ITripDataService _tripDataService;
    private readonly ICollaboratorDataService _collaboratorDataService;
    private readonly INotificationDataService _notificationDataService;
    private readonly IFriendDataService _friendDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly ILogger<PersonsController> _logger;

    public PersonsController(
        IPersonDataService personDataService,
        ITripDataService tripDataService,
        ICollaboratorDataService collaboratorDataService,
        INotificationDataService notificationDataService,
        IFriendDataService friendDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        ILogger<PersonsController> logger)
    {
        _personDataService = personDataService;
        _tripDataService = tripDataService;
        _collaboratorDataService = collaboratorDataService;
        _notificationDataService = notificationDataService;
        _friendDataService = friendDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetPersonsByTrip(string tripId)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var hasAccess = await _tripDataService.HasUserTripAccessAsync(tripId, userId);
            if (!hasAccess)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to view members for this trip."));

            var persons = await _personDataService.GetPersonsByTripAsync(tripId);
            return Ok(ApiResponse<IEnumerable<PersonDto>>.Ok(persons));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetPersonsByTrip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch members."));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPersonById(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var person = await _personDataService.GetPersonByIdAsync(id);
            if (person == null)
                return NotFound(ApiResponse.Fail("Person not found."));

            var hasAccess = await _tripDataService.HasUserTripAccessAsync(person.TripId, userId);
            if (!hasAccess)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to view this member."));

            return Ok(ApiResponse<PersonDto>.Ok(person));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetPersonById: {PersonId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch member."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreatePerson([FromBody] CreatePersonDto dto)
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
                return StatusCode(403, ApiResponse.Fail("You do not have permission to add members to this trip."));

            var person = await _personDataService.CreatePersonAsync(dto);
            await _activityLogDataService.LogActivityAsync(userId, dto.TripId, "ADD_PERSON", $"Added member '{dto.Name}'");
            _logger.LogInformation("Member {PersonId} '{Name}' added to trip {TripId}", person.PersonId, dto.Name, dto.TripId);

            // If adding an existing user or friend, send trip invitation notification and create pending collaborator record
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
                    _logger.LogWarning(ex, "Error resolving user for phone {Phone}", dto.PhoneNumber);
                }
            }

            if (!string.IsNullOrEmpty(targetUserId) && targetUserId != userId)
            {
                try
                {
                    await _collaboratorDataService.AddOrUpdatePendingCollaboratorAsync(dto.TripId, targetUserId, person.PersonId, dto.PhoneNumber);
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
                    _logger.LogInformation("Trip invitation notification sent to {TargetUserId} for trip {TripId}", targetUserId, dto.TripId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending trip invitation notification to user {TargetUserId}", targetUserId);
                }
            }

            return Ok(ApiResponse<Person>.Ok(person, "Member added successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CreatePerson for trip: {TripId}", dto.TripId);
            return StatusCode(500, ApiResponse.Fail("Failed to add member."));
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePerson(string id, [FromBody] UpdatePersonDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(ApiResponse.Fail("Person name is required."));

            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var existing = await _personDataService.GetPersonByIdAsync(id);
            if (existing == null)
                return NotFound(ApiResponse.Fail("Person not found."));

            var canManage = await _tripDataService.CanUserManageTripAsync(existing.TripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to modify this member."));

            var updated = await _personDataService.UpdatePersonAsync(id, dto);
            if (!updated)
                return NotFound(ApiResponse.Fail("Person not found."));

            try
            {
                await _personDataService.RecalculateTripBalancesAsync(existing.TripId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not recalculate balances after updating person {PersonId}", id);
            }

            _logger.LogInformation("Member {PersonId} updated", id);
            return Ok(ApiResponse.Ok("Member updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdatePerson: {PersonId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update member."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePerson(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var existing = await _personDataService.GetPersonByIdAsync(id);
            if (existing == null)
                return NotFound(ApiResponse.Fail("Person not found."));

            var canManage = await _tripDataService.CanUserManageTripAsync(existing.TripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to delete this member."));

            var deleted = await _personDataService.DeletePersonAsync(id);
            if (!deleted)
                return NotFound(ApiResponse.Fail("Person not found."));

            _logger.LogInformation("Member {PersonId} removed", id);
            return Ok(ApiResponse.Ok("Member removed and balances updated."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeletePerson: {PersonId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to remove member."));
        }
    }

    [HttpPost("restore/{id}")]
    public async Task<IActionResult> RestorePerson(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var restored = await _personDataService.RestorePersonAsync(id);
            if (!restored)
                return NotFound(ApiResponse.Fail("Person not found or could not be restored."));

            _logger.LogInformation("Member {PersonId} restored", id);
            return Ok(ApiResponse.Ok("Member restored successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in RestorePerson: {PersonId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to restore member."));
        }
    }

    [HttpPost("recalculate/{tripId}")]
    public async Task<IActionResult> RecalculateTrip(string tripId)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var canManage = await _tripDataService.CanUserManageTripAsync(tripId, userId);
            if (!canManage)
                return StatusCode(403, ApiResponse.Fail("You do not have permission to recalculate balances for this trip."));

            await _personDataService.RecalculateTripBalancesAsync(tripId);
            var persons = await _personDataService.GetPersonsByTripAsync(tripId);
            _logger.LogInformation("Balances recalculated for trip {TripId}", tripId);
            return Ok(ApiResponse<IEnumerable<PersonDto>>.Ok(persons, "Balances recalculated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in RecalculateTrip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to recalculate balances."));
        }
    }
}
