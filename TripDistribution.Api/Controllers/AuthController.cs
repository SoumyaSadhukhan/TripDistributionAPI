using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Auth;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthDataService _authDataService;
    private readonly IUserDataService _userDataService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthDataService authDataService,
        IUserDataService userDataService,
        IJwtTokenService jwtTokenService,
        ICurrentUserContext currentUserContext,
        ILogger<AuthController> logger)
    {
        _authDataService = authDataService;
        _userDataService = userDataService;
        _jwtTokenService = jwtTokenService;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLower();
    }

    [HttpPost("send-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Phone))
                return BadRequest(ApiResponse.Fail("Phone number is required."));

            if (dto.Purpose.Equals("REGISTER", StringComparison.OrdinalIgnoreCase))
            {
                var existing = await _authDataService.GetUserByPhoneAsync(dto.Phone);
                if (existing != null)
                    return BadRequest(ApiResponse.Fail("An account with this phone number already exists. Please log in."));
            }

            if (dto.Purpose.Equals("FORGOT_PASSWORD", StringComparison.OrdinalIgnoreCase))
            {
                var existing = await _authDataService.GetUserByPhoneAsync(dto.Phone);
                if (existing == null)
                    return NotFound(ApiResponse.Fail("No account found with this phone number."));
            }

            var otpCode = Random.Shared.Next(100000, 999999).ToString();
            var saved = await _authDataService.SaveOtpAsync(dto.Phone, otpCode, dto.Purpose.ToUpper());

            if (!saved)
                return StatusCode(500, ApiResponse.Fail("Failed to generate OTP."));

            _logger.LogInformation("OTP dispatched for phone: {Phone}, purpose: {Purpose}", dto.Phone, dto.Purpose);
            return Ok(ApiResponse<object>.Ok(new { Phone = dto.Phone, OtpCode = otpCode, Message = "OTP sent successfully." }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in SendOtp for phone: {Phone}", dto.Phone);
            return StatusCode(500, ApiResponse.Fail("Failed to process OTP request."));
        }
    }

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Phone) || string.IsNullOrWhiteSpace(dto.OtpCode))
                return BadRequest(ApiResponse.Fail("Phone and OTP code are required."));

            var isVerified = await _authDataService.VerifyOtpAsync(dto.Phone, dto.OtpCode, dto.Purpose.ToUpper());
            if (!isVerified)
                return BadRequest(ApiResponse.Fail("Invalid or expired OTP code."));

            _logger.LogInformation("OTP verified for phone: {Phone}", dto.Phone);
            return Ok(ApiResponse.Ok("OTP verified successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in VerifyOtp for phone: {Phone}", dto.Phone);
            return StatusCode(500, ApiResponse.Fail("Failed to verify OTP."));
        }
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Phone) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(ApiResponse.Fail("Phone and password are required."));

            var existing = await _authDataService.GetUserByPhoneAsync(dto.Phone);
            if (existing != null)
                return BadRequest(ApiResponse.Fail("User with this phone number already exists."));

            var passwordHash = HashPassword(dto.Password);
            var user = await _authDataService.CreateUserAsync(dto, passwordHash);

            var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

            await _authDataService.SaveUserTokenAsync(new UserToken
            {
                TokenId = Guid.NewGuid().ToString(),
                UserId = user.UserId,
                PhoneNumber = user.PhoneNumber,
                AuthToken = token,
                DeviceInfo = dto.DeviceInfo,
                IsActive = true,
                ExpiresAt = expiresAt
            });

            _logger.LogInformation("User registered successfully: {UserId}, Phone: {Phone}", user.UserId, user.PhoneNumber);

            var response = new AuthResponseDto
            {
                UserId = user.UserId,
                Phone = user.PhoneNumber,
                FullName = user.FullName,
                Token = token,
                DietType = user.DietType,
                DietName = user.DietName,
                AvatarColor = user.AvatarColor,
                IsBiometricEnabled = user.IsBiometricEnabled,
                ExpiresAt = expiresAt
            };

            return Ok(ApiResponse<AuthResponseDto>.Ok(response, "Registration successful."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in Register for phone: {Phone}", dto.Phone);
            return StatusCode(500, ApiResponse.Fail("Failed to complete registration."));
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Phone) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(ApiResponse.Fail("Phone and password are required."));

            var user = await _authDataService.GetUserByPhoneAsync(dto.Phone);
            if (user == null)
            {
                _logger.LogWarning("Login attempt for non-existent user: {Phone}", dto.Phone);
                return Unauthorized(ApiResponse.Fail("Invalid phone number or password."));
            }

            var passwordHash = HashPassword(dto.Password);
            if (user.PasswordHash != passwordHash)
            {
                _logger.LogWarning("Invalid password attempt for phone: {Phone}", dto.Phone);
                return Unauthorized(ApiResponse.Fail("Invalid phone number or password."));
            }

            var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

            await _authDataService.SaveUserTokenAsync(new UserToken
            {
                TokenId = Guid.NewGuid().ToString(),
                UserId = user.UserId,
                PhoneNumber = user.PhoneNumber,
                AuthToken = token,
                DeviceInfo = dto.DeviceInfo,
                IsActive = true,
                ExpiresAt = expiresAt
            });

            await _authDataService.UpdateLastLoginAsync(user.UserId);
            _logger.LogInformation("User logged in successfully: {UserId}", user.UserId);

            var response = new AuthResponseDto
            {
                UserId = user.UserId,
                Phone = user.PhoneNumber,
                FullName = user.FullName,
                Token = token,
                DietType = user.DietType,
                DietName = user.DietName,
                AvatarColor = user.AvatarColor,
                IsBiometricEnabled = user.IsBiometricEnabled,
                ExpiresAt = expiresAt
            };

            return Ok(ApiResponse<AuthResponseDto>.Ok(response, "Login successful."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in Login for phone: {Phone}", dto.Phone);
            return StatusCode(500, ApiResponse.Fail("Login failed. Please try again."));
        }
    }

    [HttpPost("biometric-login")]
    [AllowAnonymous]
    public async Task<IActionResult> BiometricLogin([FromBody] BiometricLoginDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.UserId) || string.IsNullOrWhiteSpace(dto.AuthToken))
                return BadRequest(ApiResponse.Fail("UserId and AuthToken are required."));

            var tokenRecord = await _authDataService.GetTokenAsync(dto.AuthToken);
            if (tokenRecord == null || tokenRecord.UserId != dto.UserId)
                return Unauthorized(ApiResponse.Fail("Invalid or expired session. Please log in with password."));

            var user = await _authDataService.GetUserByIdAsync(dto.UserId);
            if (user == null)
                return Unauthorized(ApiResponse.Fail("User not found."));

            var (newToken, expiresAt) = _jwtTokenService.GenerateToken(user);

            await _authDataService.SaveUserTokenAsync(new UserToken
            {
                TokenId = Guid.NewGuid().ToString(),
                UserId = user.UserId,
                PhoneNumber = user.PhoneNumber,
                AuthToken = newToken,
                DeviceInfo = dto.DeviceInfo,
                IsActive = true,
                ExpiresAt = expiresAt
            });

            await _authDataService.UpdateLastLoginAsync(user.UserId);
            _logger.LogInformation("Biometric login successful for user: {UserId}", user.UserId);

            var response = new AuthResponseDto
            {
                UserId = user.UserId,
                Phone = user.PhoneNumber,
                FullName = user.FullName,
                Token = newToken,
                DietType = user.DietType,
                DietName = user.DietName,
                AvatarColor = user.AvatarColor,
                IsBiometricEnabled = user.IsBiometricEnabled,
                ExpiresAt = expiresAt
            };

            return Ok(ApiResponse<AuthResponseDto>.Ok(response, "Biometric login successful."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in BiometricLogin for user: {UserId}", dto.UserId);
            return StatusCode(500, ApiResponse.Fail("Biometric login failed."));
        }
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Phone) || string.IsNullOrWhiteSpace(dto.OtpCode) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(ApiResponse.Fail("Phone, OTP code, and new password are required."));

            var isVerified = await _authDataService.VerifyOtpAsync(dto.Phone, dto.OtpCode, "FORGOT_PASSWORD");
            if (!isVerified)
                return BadRequest(ApiResponse.Fail("Invalid or expired OTP verification code."));

            var newHash = HashPassword(dto.NewPassword);
            var updated = await _authDataService.UpdatePasswordAsync(dto.Phone, newHash);
            if (!updated)
                return NotFound(ApiResponse.Fail("Failed to update password. User not found."));

            _logger.LogInformation("Password reset for phone: {Phone}", dto.Phone);
            return Ok(ApiResponse.Ok("Password reset successfully. Please log in with your new password."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ResetPassword for phone: {Phone}", dto.Phone);
            return StatusCode(500, ApiResponse.Fail("Password reset failed."));
        }
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized(ApiResponse.Fail("Unauthorized"));

            var profile = await _userDataService.GetProfileAsync(userId);
            if (profile == null) return NotFound(ApiResponse.Fail("Profile not found."));

            return Ok(ApiResponse<UserProfileDto>.Ok(profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetProfile");
            return StatusCode(500, ApiResponse.Fail("Failed to retrieve profile."));
        }
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized(ApiResponse.Fail("Unauthorized"));

            var updated = await _userDataService.UpdateProfileAsync(userId, dto);
            if (!updated) return BadRequest(ApiResponse.Fail("Failed to update profile."));

            var profile = await _userDataService.GetProfileAsync(userId);
            _logger.LogInformation("Profile updated for user: {UserId}", userId);
            return Ok(ApiResponse<UserProfileDto>.Ok(profile!, "Profile updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateProfile");
            return StatusCode(500, ApiResponse.Fail("Failed to update profile."));
        }
    }

    [HttpPut("diet")]
    [Authorize]
    public async Task<IActionResult> UpdateDiet([FromBody] UpdateDietDto dto)
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized(ApiResponse.Fail("Unauthorized"));

            var updated = await _userDataService.UpdateDietAsync(userId, dto.DietType, dto.DietName);
            if (!updated) return BadRequest(ApiResponse.Fail("Failed to update dietary preference."));

            _logger.LogInformation("Diet updated for user: {UserId}", userId);
            return Ok(ApiResponse.Ok("Dietary preference updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in UpdateDiet");
            return StatusCode(500, ApiResponse.Fail("Failed to update diet preference."));
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var authHeader = Request.Headers.Authorization.ToString();
            var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) 
                ? authHeader[7..] 
                : null;

            if (!string.IsNullOrEmpty(token))
            {
                await _authDataService.InvalidateTokenAsync(token);
                _logger.LogInformation("User logged out, token invalidated");
            }

            return Ok(ApiResponse.Ok("Logged out successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in Logout");
            return StatusCode(500, ApiResponse.Fail("Logout encountered an error."));
        }
    }
}
