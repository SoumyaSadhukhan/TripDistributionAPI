using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.DTOs.Friends;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly IFriendDataService _friendDataService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<FriendsController> _logger;

    public FriendsController(
        IFriendDataService friendDataService,
        ICurrentUserContext currentUserContext,
        ILogger<FriendsController> logger)
    {
        _friendDataService = friendDataService;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyFriends()
    {
        try
        {
            var userId = _currentUserContext.UserId;
            var phone = _currentUserContext.PhoneNumber;

            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var friends = await _friendDataService.GetFriendsAsync(userId, phone);
            return Ok(ApiResponse<IEnumerable<FriendDto>>.Ok(friends));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetMyFriends for user: {UserId}", _currentUserContext.UserId);
            return StatusCode(500, ApiResponse.Fail("Failed to fetch friends."));
        }
    }

    [HttpPost("check-contacts")]
    public async Task<IActionResult> CheckContacts([FromBody] CheckContactsDto dto)
    {
        try
        {
            var matches = await _friendDataService.CheckContactsAsync(dto.Contacts);
            return Ok(ApiResponse<IEnumerable<ContactMatchResultDto>>.Ok(matches));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CheckContacts");
            return StatusCode(500, ApiResponse.Fail("Failed to check contacts."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddOrInviteFriend([FromBody] FriendInviteDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.FriendPhone) || string.IsNullOrWhiteSpace(dto.FriendName))
                return BadRequest(ApiResponse.Fail("Friend phone and name are required."));

            var userId = _currentUserContext.UserId;
            var phone = _currentUserContext.PhoneNumber;

            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var result = await _friendDataService.AddOrInviteFriendAsync(userId, phone, dto);
            _logger.LogInformation("Friend added: {FriendPhone} by user {UserId}", dto.FriendPhone, userId);

            return Ok(ApiResponse<FriendDto>.Ok(result, "Friend added successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in AddOrInviteFriend for phone: {FriendPhone}", dto.FriendPhone);
            return StatusCode(500, ApiResponse.Fail("Failed to add friend."));
        }
    }

    [HttpPost("search")]
    public IActionResult SearchUsers([FromBody] SearchFriendsDto dto)
    {
        // Dummy implementation to satisfy Flutter app
        return Ok(new { success = true, users = new object[] { } });
    }

    [HttpGet("requests")]
    public IActionResult GetFriendRequests([FromQuery] string userId)
    {
        // Dummy implementation to satisfy Flutter app
        return Ok(new { success = true, requests = new object[] { } });
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateFriendStatusDto dto)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var updated = await _friendDataService.UpdateFriendStatusAsync(id, userId, dto.Status);
            if (!updated)
                return NotFound(ApiResponse.Fail("Friend connection not found or unauthorized."));

            _logger.LogInformation("Friend connection {ConnectionId} status updated to {Status}", id, dto.Status);
            return Ok(ApiResponse.Ok("Friend connection status updated."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateStatus for connection: {ConnectionId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to update status."));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFriend(string id)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse.Fail("Unauthorized user."));

            var deleted = await _friendDataService.DeleteFriendAsync(id, userId);
            if (!deleted)
                return NotFound(ApiResponse.Fail("Friend connection not found or unauthorized."));

            _logger.LogInformation("Friend connection {ConnectionId} deleted", id);
            return Ok(ApiResponse.Ok("Friend connection removed."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in DeleteFriend for connection: {ConnectionId}", id);
            return StatusCode(500, ApiResponse.Fail("Failed to remove friend."));
        }
    }
}


