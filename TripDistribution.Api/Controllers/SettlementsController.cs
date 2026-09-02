using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Settlements;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettlementsController : ControllerBase
{
    private readonly ISettlementDataService _settlementDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IActivityLogDataService _activityLogDataService;
    private readonly INotificationDataService _notificationDataService;
    private readonly IPersonDataService _personDataService;
    private readonly ILogger<SettlementsController> _logger;

    public SettlementsController(
        ISettlementDataService settlementDataService,
        ICurrentUserContext currentUserContext,
        IActivityLogDataService activityLogDataService,
        INotificationDataService notificationDataService,
        IPersonDataService personDataService,
        ILogger<SettlementsController> logger)
    {
        _settlementDataService = settlementDataService;
        _currentUserContext = currentUserContext;
        _activityLogDataService = activityLogDataService;
        _notificationDataService = notificationDataService;
        _personDataService = personDataService;
        _logger = logger;
    }

    [HttpGet("trip/{tripId}")]
    public async Task<IActionResult> GetSettlementsByTrip(string tripId)
    {
        try
        {
            var settlements = await _settlementDataService.GetSettlementsByTripAsync(tripId);
            return Ok(ApiResponse<IEnumerable<SettlementDto>>.Ok(settlements));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetSettlementsByTrip: {TripId}", tripId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch settlements."));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetSettlementById(string id)
    {
        try
        {
            var settlement = await _settlementDataService.GetSettlementByIdAsync(id);
            if (settlement == null)
                return NotFound(ApiResponse.Fail("Settlement not found."));

            return Ok(ApiResponse<SettlementDto>.Ok(settlement));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetSettlementById: {SettlementId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch settlement details."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> ProposeSettlement([FromBody] ProposeSettlementDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.TripId) || string.IsNullOrWhiteSpace(dto.FromPersonId) || string.IsNullOrWhiteSpace(dto.ToPersonId) || dto.Amount <= 0)
                return BadRequest(ApiResponse.Fail("TripId, FromPersonId, ToPersonId, and valid Amount (> 0) are required."));

            var userId = _currentUserContext.UserId;
            var settlement = await _settlementDataService.ProposeSettlementAsync(userId ?? "", dto);

            await _activityLogDataService.LogActivityAsync(userId, dto.TripId, "SETTLE_DEBT", $"{settlement.FromPersonName} paid {settlement.ToPersonName} {dto.Amount:N2}");
            _logger.LogInformation("Settlement {SettlementId} recorded for trip {TripId}", settlement.SettlementId, dto.TripId);

            var toPerson = await _personDataService.GetPersonByIdAsync(dto.ToPersonId);
            if (toPerson != null && !string.IsNullOrWhiteSpace(toPerson.UserId))
            {
                await _notificationDataService.CreateNotificationAsync(new Notification
                {
                    UserId = toPerson.UserId,
                    TripId = dto.TripId,
                    Title = "Settlement Payment Recorded",
                    Message = $"{settlement.FromPersonName} paid you {dto.Amount:N2}",
                    Type = "SETTLEMENT",
                    Amount = dto.Amount
                });
            }

            return CreatedAtAction(nameof(GetSettlementById), new { id = settlement.SettlementId }, ApiResponse<SettlementDto>.Ok(settlement, "Settlement recorded successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProposeSettlement for trip: {TripId}", dto.TripId);
            return StatusCode(500, ApiResponse.Fail("Failed to record settlement."));
        }
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateSettlementStatus(string id, [FromBody] UpdateSettlementStatusDto dto)
    {
        try
        {
            var updated = await _settlementDataService.UpdateSettlementStatusAsync(id, dto.Status);
            if (!updated)
                return NotFound(ApiResponse.Fail("Settlement not found."));

            var settlement = await _settlementDataService.GetSettlementByIdAsync(id);
            await _activityLogDataService.LogActivityAsync(_currentUserContext.UserId, settlement?.TripId, "SETTLEMENT_STATUS", $"Settlement status updated to '{dto.Status}'");
            _logger.LogInformation("Settlement {SettlementId} status updated to {Status}", id, dto.Status);

            return Ok(ApiResponse<SettlementDto>.Ok(settlement!, "Settlement status updated."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateSettlementStatus: {SettlementId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update settlement status."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSettlement(string id)
    {
        try
        {
            var existing = await _settlementDataService.GetSettlementByIdAsync(id);
            if (existing == null)
                return NotFound(ApiResponse.Fail("Settlement not found."));

            var deleted = await _settlementDataService.DeleteSettlementAsync(id);
            if (!deleted)
                return NotFound(ApiResponse.Fail("Settlement could not be deleted."));

            await _activityLogDataService.LogActivityAsync(_currentUserContext.UserId, existing.TripId, "DELETE_SETTLEMENT", $"Deleted settlement of {existing.Amount:N2}");
            _logger.LogInformation("Settlement {SettlementId} deleted", id);

            return Ok(ApiResponse.Ok("Settlement deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeleteSettlement: {SettlementId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to delete settlement."));
        }
    }
}
