using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Trips;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly ITripDataService _tripDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly ILogger<TripsController> _logger;

    public TripsController(
        ITripDataService tripDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        ILogger<TripsController> logger)
    {
        _tripDataService = tripDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyTrips()
    {
        try
        {
            var userId = _currentUserContext.UserId;
            var phone = _currentUserContext.PhoneNumber;

            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var trips = await _tripDataService.GetUserTripsAsync(userId, phone);
            return Ok(ApiResponse<IEnumerable<TripSummaryDto>>.Ok(trips));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetMyTrips for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch trips."));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTripById(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            // Enforce BOLA authorization check
            if (!await _tripDataService.HasUserTripAccessAsync(id, userId, _currentUserContext.PhoneNumber))
                return StatusCode(403, ApiResponse.Fail("You do not have access to this trip."));

            var trip = await _tripDataService.GetTripDetailAsync(id, userId);
            if (trip == null)
                return NotFound(ApiResponse.Fail("Trip not found."));

            return Ok(ApiResponse<TripDetailDto>.Ok(trip));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetTripById: {TripId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch trip details."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateTrip([FromBody] CreateTripDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(ApiResponse.Fail("Trip name is required."));

            if (dto.Name.Length > 100)
                return BadRequest(ApiResponse.Fail("Trip name cannot exceed 100 characters."));

            if (dto.Description != null && dto.Description.Length > 500)
                return BadRequest(ApiResponse.Fail("Trip description cannot exceed 500 characters."));

            var userId = _currentUserContext.UserId;
            var phone = _currentUserContext.PhoneNumber;
            var name = _currentUserContext.FullName;
            var dietType = _currentUserContext.DietType;

            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var trip = await _tripDataService.CreateTripAsync(userId, phone, name, dietType, dietType.ToString(), dto);

            await _activityLogDataService.LogActivityAsync(userId, trip.TripId, "CREATE_TRIP", $"Created trip '{dto.Name}'");
            _logger.LogInformation("Trip {TripId} created by user {UserId}", trip.TripId, userId);

            return CreatedAtAction(nameof(GetTripById), new { id = trip.TripId }, ApiResponse<TripSummaryDto>.Ok(new TripSummaryDto
            {
                TripId = trip.TripId,
                UserId = trip.UserId,
                Name = trip.Name,
                Description = trip.Description,
                CurrencyCode = trip.CurrencyCode,
                MemberCount = 1,
                TotalExpenses = 0,
                IsOwner = true,
                Role = "OWNER",
                CreatedAt = trip.CreatedAt,
                UpdatedAt = trip.UpdatedAt
            }, "Trip created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CreateTrip for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to create trip."));
        }
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncTrips([FromBody] SyncTripsDto dto)
    {
        try
        {
            var currentUserId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            // Use verified JWT user identity instead of client-provided userId
            var jsonString = System.Text.Json.JsonSerializer.Serialize(dto.Trips);
            var success = await _tripDataService.SyncTripsAsync(currentUserId, jsonString);

            if (!success)
                return StatusCode(500, ApiResponse.Fail("Failed to synchronize trips."));

            return Ok(new { success = true, message = "Trips synchronized successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in SyncTrips for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to synchronize trips."));
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTrip(string id, [FromBody] UpdateTripDto dto)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(ApiResponse.Fail("Trip name is required."));

            if (dto.Name.Length > 100)
                return BadRequest(ApiResponse.Fail("Trip name cannot exceed 100 characters."));

            if (dto.Description != null && dto.Description.Length > 500)
                return BadRequest(ApiResponse.Fail("Trip description cannot exceed 500 characters."));

            // Verify permission (Owner, Admin or Editor)
            if (!await _tripDataService.CanUserManageTripAsync(id, userId))
                return StatusCode(403, ApiResponse.Fail("You do not have permission to modify this trip."));

            var updated = await _tripDataService.UpdateTripAsync(id, dto);
            if (!updated)
                return NotFound(ApiResponse.Fail("Trip not found or could not be updated."));

            await _activityLogDataService.LogActivityAsync(userId, id, "UPDATE_TRIP", $"Updated trip info '{dto.Name}'");
            _logger.LogInformation("Trip {TripId} updated", id);

            return Ok(ApiResponse.Ok("Trip updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateTrip: {TripId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update trip."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTrip(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            // Only Trip Owner can delete the trip
            if (!await _tripDataService.IsTripOwnerAsync(id, userId))
                return StatusCode(403, ApiResponse.Fail("Only the trip owner can delete this trip."));

            var deleted = await _tripDataService.DeleteTripAsync(id);
            if (!deleted)
                return NotFound(ApiResponse.Fail("Trip not found."));

            await _activityLogDataService.LogActivityAsync(userId, id, "DELETE_TRIP", $"Deleted trip {id}");
            _logger.LogInformation("Trip {TripId} deleted", id);

            return Ok(ApiResponse.Ok("Trip and all associated records deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeleteTrip: {TripId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to delete trip."));
        }
    }
}
