using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TripDistribution.Api.Services;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Auth;
using TripDistribution.Model.DTOs.Common;
using TripDistribution.Model.Entities;

namespace TripDistribution.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("auth-policy")]
public class AuthController : ControllerBase
{
    private readonly IAuthDataService _authDataService;
    private readonly IUserDataService _userDataService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly IPhoneVerificationService _phoneVerificationService;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthDataService authDataService,
        IUserDataService userDataService,
        IJwtTokenService jwtTokenService,
        ICurrentUserContext currentUserContext,
        IPasswordHasher passwordHasher,
        IEmailService emailService,
        IPhoneVerificationService phoneVerificationService,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AuthController> logger)
    {
        _authDataService = authDataService;
        _userDataService = userDataService;
        _jwtTokenService = jwtTokenService;
        _currentUserContext = currentUserContext;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _phoneVerificationService = phoneVerificationService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user. Public endpoint.
    /// Creates user, generates unique UserId, sends verification OTP to email, and returns Bearer token.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        try
        {
            if ((string.IsNullOrWhiteSpace(dto.Phone) && string.IsNullOrWhiteSpace(dto.Email)) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(ApiResponse.Fail("Email or Phone, and Password are required."));

            if (dto.Password.Length < 6)
                return BadRequest(ApiResponse.Fail("Password must be at least 6 characters long."));

            // 1. One registration time check: Validate mobile phone number via Veriphone (Bypassed in DEBUG)
            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                var fullPhone = !string.IsNullOrWhiteSpace(dto.CountryCode) && !dto.Phone.StartsWith("+")
                    ? $"{dto.CountryCode.Trim()}{dto.Phone.Trim()}"
                    : dto.Phone.Trim();

                var (isValidPhone, phoneError, e164, carrier) = await _phoneVerificationService.VerifyPhoneAsync(fullPhone);
                if (!isValidPhone)
                {
                    return BadRequest(ApiResponse.Fail(phoneError ?? "The phone number provided is not a valid mobile number."));
                }

                if (!string.IsNullOrWhiteSpace(e164))
                {
                    dto.Phone = e164;
                }
            }

            var existing = await _authDataService.FindUserAsync(null, dto.Username, dto.Email, dto.Phone);
            if (existing != null)
                return BadRequest(ApiResponse.Fail("An account with this email, phone, or username already exists."));

            // Secure PBKDF2 Password Hashing
            var passwordHash = _passwordHasher.HashPassword(dto.Password);

            // 2. Create user row in database and generate UserId
            var user = await _authDataService.CreateUserAsync(dto, passwordHash);

            // 3. Dispatch verification OTP via email if email is provided
            bool emailSent = false;
            string? otpCode = null;
            if (!string.IsNullOrWhiteSpace(user.Email) && user.Email.Contains('@'))
            {
                var isDev = _environment.IsDevelopment();
                var bypassInDebug = isDev && _configuration.GetValue<bool>("AuthSettings:BypassOtpInDebug", false);
                otpCode = bypassInDebug ? "123456" : RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                await _authDataService.SaveOtpAsync(user.UserId, user.Email, user.PhoneNumber, otpCode, "REGISTER");
                if (!bypassInDebug)
                {
                    emailSent = await _emailService.SendOtpEmailAsync(user.Email, otpCode, "REGISTER", user.FullName);
                }
                else
                {
                    emailSent = true;
                }
            }

            // 3. Generate Bearer JWT token (with unique JTI nonce)
            var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

            // 4. Save token in database UserTokens table
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

            _logger.LogInformation("User registered successfully: {UserId}, Email: {Email}, Phone: {Phone}", user.UserId, user.Email, user.PhoneNumber);

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

            var msg = emailSent 
                ? $"User registered successfully. Verification OTP sent to {user.Email}." 
                : "Registration successful.";

            return Ok(ApiResponse<AuthResponseDto>.Ok(response, msg));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in Register");
            return StatusCode(500, ApiResponse.Fail("Failed to complete registration."));
        }
    }

    /// <summary>
    /// User Login. Public endpoint.
    /// Authenticates credentials, revokes previous sessions, issues a fresh Bearer token, and records it in UserTokens.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Password) || 
               (string.IsNullOrWhiteSpace(dto.Phone) && string.IsNullOrWhiteSpace(dto.Email) && string.IsNullOrWhiteSpace(dto.Username) && string.IsNullOrWhiteSpace(dto.UserId)))
            {
                return BadRequest(ApiResponse.Fail("Username, Email, Phone, or UserId and Password are required."));
            }

            var user = await _authDataService.FindUserAsync(dto.UserId, dto.Username, dto.Email, dto.Phone);
            if (user == null)
            {
                _logger.LogWarning("Login attempt for non-existent user identifier");
                return Unauthorized(ApiResponse.Fail("Invalid credentials."));
            }

            // Verify password using PBKDF2 (with backward-compatibility for legacy SHA-256)
            if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.VerifyPassword(dto.Password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid password attempt for user: {UserId}", user.UserId);
                return Unauthorized(ApiResponse.Fail("Invalid credentials."));
            }

            // Automatic hash upgrade to PBKDF2 if still using legacy SHA-256
            if (!user.PasswordHash.StartsWith("$pbkdf2$"))
            {
                var newSaltedHash = _passwordHasher.HashPassword(dto.Password);
                await _authDataService.UpdatePasswordAsync(user.PhoneNumber, newSaltedHash);
                _logger.LogInformation("Upgraded password hash to PBKDF2 for user {UserId}", user.UserId);
            }

            // Revoke all previous active sessions in database for strict single-session security
            await _authDataService.InvalidateAllUserTokensAsync(user.UserId);

            // Generate fresh JWT Bearer token with unique JTI nonce
            var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

            // Persist the new active token in UserTokens table
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
            _logger.LogInformation("User logged in successfully: {UserId}, previous tokens revoked", user.UserId);

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
            _logger.LogError(ex, "Exception in Login");
            return StatusCode(500, ApiResponse.Fail("Login failed. Please try again."));
        }
    }

    /// <summary>
    /// Biometric Login using active device token.
    /// </summary>
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

            // Rotate token: invalidate old session
            await _authDataService.InvalidateTokenAsync(dto.AuthToken);
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

    /// <summary>
    /// Send OTP to user's registered email or phone. Open to unauthenticated users for FORGOT_PASSWORD and REGISTER.
    /// Strict BOLA Enforcement: If authenticated, user cannot request OTP for another user.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-policy")]
    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        try
        {
            var authenticatedUserId = _currentUserContext.UserId;

            // Strict BOLA Guard: Authenticated user cannot trigger OTP for another account
            if (!string.IsNullOrWhiteSpace(dto.UserId) && !string.IsNullOrWhiteSpace(authenticatedUserId) && dto.UserId != authenticatedUserId)
            {
                _logger.LogWarning("BOLA blocked: Authenticated user {AuthUser} attempted to request OTP for target {TargetUser}", authenticatedUserId, dto.UserId);
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("You are not authorized to request OTP for another user account."));
            }

            var targetUserId = !string.IsNullOrWhiteSpace(authenticatedUserId) ? authenticatedUserId : dto.UserId;

            // Resolve target user
            User? targetUser = null;
            if (!string.IsNullOrWhiteSpace(targetUserId) || !string.IsNullOrWhiteSpace(dto.Username) || !string.IsNullOrWhiteSpace(dto.Email) || !string.IsNullOrWhiteSpace(dto.Phone))
            {
                targetUser = await _authDataService.FindUserAsync(targetUserId, dto.Username, dto.Email, dto.Phone);
            }

            var configuredEnv = (_configuration["Environment"] ?? "DEBUG").Trim().ToUpperInvariant();
            var isProductionMode = !string.Equals(dto.Environment, "debug", StringComparison.OrdinalIgnoreCase) &&
                                   (string.Equals(configuredEnv, "PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(dto.Environment, "production", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(dto.Environment, "prod", StringComparison.OrdinalIgnoreCase));

            if (targetUser == null)
            {
                // Account Enumeration Defense: For FORGOT_PASSWORD, return generic success so account existence is not leaked
                if (string.Equals(dto.Purpose, "FORGOT_PASSWORD", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Account enumeration defense: Silent discard for non-existent target during FORGOT_PASSWORD");
                    return Ok(ApiResponse<object>.Ok(new
                    {
                        EmailSent = true,
                        Message = "If an active account matches the provided details, a verification code has been dispatched."
                    }));
                }

                if (string.Equals(dto.Purpose, "REGISTER", StringComparison.OrdinalIgnoreCase))
                {
                    string? regEmail = !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : null;
                    string? regPhone = !string.IsNullOrWhiteSpace(dto.Phone) ? dto.Phone : null;
                    if (string.IsNullOrWhiteSpace(regEmail) && string.IsNullOrWhiteSpace(regPhone))
                    {
                        return BadRequest(ApiResponse.Fail("Email address is required to receive registration verification OTP."));
                    }

                    var isDev = _environment.IsDevelopment();
                    var bypassInDebug = !isProductionMode && isDev && _configuration.GetValue<bool>("AuthSettings:BypassOtpInDebug", false);
                    var regOtpCode = bypassInDebug ? "123456" : RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

                    var regSaved = await _authDataService.SaveOtpAsync(null, regEmail, regPhone, regOtpCode, "REGISTER");
                    if (!regSaved)
                        return StatusCode(500, ApiResponse.Fail("Failed to generate OTP."));

                    bool regEmailSent = false;
                    if (!string.IsNullOrWhiteSpace(regEmail) && regEmail.Contains('@'))
                    {
                        if (isProductionMode || !bypassInDebug)
                        {
                            regEmailSent = await _emailService.SendOtpEmailAsync(regEmail, regOtpCode, "REGISTER", dto.Username ?? "Member");
                        }
                        else
                        {
                            regEmailSent = true;
                        }
                    }

                    var exposeOtp = !isProductionMode && isDev && _configuration.GetValue<bool>("AuthSettings:ExposeOtpInResponse", false);
                    var regResponseData = new
                    {
                        Email = regEmail,
                        Phone = regPhone,
                        EmailSent = regEmailSent,
                        OtpCode = exposeOtp ? regOtpCode : null,
                        Message = regEmailSent ? $"Verification OTP sent successfully to email: {regEmail}" : "OTP generated successfully."
                    };

                    return Ok(ApiResponse<object>.Ok(regResponseData));
                }

                return NotFound(ApiResponse.Fail("No valid user account found with the provided details."));
            }

            // If targetUser exists and purpose is REGISTER
            if (string.Equals(dto.Purpose, "REGISTER", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse.Fail("An account with this email or phone number already exists. Please sign in instead."));
            }

            string? destEmail = !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : targetUser.Email;
            string? destPhone = !string.IsNullOrWhiteSpace(dto.Phone) ? dto.Phone : targetUser.PhoneNumber;
            string displayName = targetUser.FullName ?? targetUser.Username ?? "Member";

            var isDevTarget = _environment.IsDevelopment();
            var bypassInDebugTarget = !isProductionMode && isDevTarget && _configuration.GetValue<bool>("AuthSettings:BypassOtpInDebug", false);
            var otpCode = bypassInDebugTarget ? "123456" : RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

            var saved = await _authDataService.SaveOtpAsync(targetUser.UserId, destEmail, destPhone, otpCode, dto.Purpose.ToUpper());
            if (!saved)
                return StatusCode(500, ApiResponse.Fail("Failed to generate OTP."));

            bool emailSent = false;
            if (!string.IsNullOrWhiteSpace(destEmail) && destEmail.Contains('@'))
            {
                if (isProductionMode || !bypassInDebugTarget)
                {
                    emailSent = await _emailService.SendOtpEmailAsync(destEmail, otpCode, dto.Purpose, displayName);
                }
                else
                {
                    emailSent = true;
                }
            }

            _logger.LogInformation("OTP dispatched: UserId: {UserId}, Email: {Email}, Phone: {Phone}, EmailSent: {EmailSent}", 
                targetUser.UserId, destEmail, destPhone, emailSent);

            var exposeOtpTarget = !isProductionMode && isDevTarget && _configuration.GetValue<bool>("AuthSettings:ExposeOtpInResponse", false);
            var responseData = new
            {
                UserId = targetUser.UserId,
                Email = destEmail,
                Phone = destPhone,
                EmailSent = emailSent,
                OtpCode = exposeOtpTarget ? otpCode : null,
                Message = emailSent ? $"OTP sent successfully to email: {destEmail}" : "OTP generated successfully."
            };

            return Ok(ApiResponse<object>.Ok(responseData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in SendOtp");
            return StatusCode(500, ApiResponse.Fail("Failed to process OTP request."));
        }
    }

    /// <summary>
    /// Verify OTP code. Open to unauthenticated users for account verification.
    /// Strict BOLA Enforcement: If authenticated, user cannot verify OTP for another user.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-policy")]
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.OtpCode))
                return BadRequest(ApiResponse.Fail("OTP code is required."));

            var authenticatedUserId = _currentUserContext.UserId;

            // Strict BOLA Guard: Authenticated user cannot verify OTP for another account
            if (!string.IsNullOrWhiteSpace(dto.UserId) && !string.IsNullOrWhiteSpace(authenticatedUserId) && dto.UserId != authenticatedUserId)
            {
                _logger.LogWarning("BOLA blocked: Authenticated user {AuthUser} attempted to verify OTP for target {TargetUser}", authenticatedUserId, dto.UserId);
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("You are not authorized to verify OTP for another user account."));
            }

            var targetUserId = !string.IsNullOrWhiteSpace(authenticatedUserId) ? authenticatedUserId : dto.UserId;

            var isDev = _environment.IsDevelopment();
            var bypassInDebug = isDev && _configuration.GetValue<bool>("AuthSettings:BypassOtpInDebug", false);
            bool isVerified;
            if (bypassInDebug && (dto.OtpCode == "123456" || dto.OtpCode == "000000"))
            {
                isVerified = true;
                if (!string.IsNullOrWhiteSpace(targetUserId) || !string.IsNullOrWhiteSpace(dto.Email) || !string.IsNullOrWhiteSpace(dto.Phone))
                {
                    await _authDataService.VerifyOtpAsync(targetUserId, dto.Email, dto.Phone, "123456", dto.Purpose.ToUpper());
                }
            }
            else
            {
                // Atomic verification prevents TOCTOU race conditions and duplicate OTP reuse
                isVerified = await _authDataService.VerifyOtpAsync(targetUserId, dto.Email, dto.Phone, dto.OtpCode, dto.Purpose.ToUpper());
            }

            if (!isVerified)
                return BadRequest(ApiResponse.Fail("Invalid or expired OTP code."));

            _logger.LogInformation("OTP verified for UserId: {UserId}, Email: {Email}, Phone: {Phone}", targetUserId, dto.Email, dto.Phone);
            return Ok(ApiResponse.Ok("OTP verified successfully. Account is active."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in VerifyOtp");
            return StatusCode(500, ApiResponse.Fail("Failed to verify OTP."));
        }
    }

    /// <summary>
    /// Reset password. Open to unauthenticated users with verified OTP.
    /// Strictly requires valid, unexpired, one-time OTP with purpose FORGOT_PASSWORD.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-policy")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        try
        {
            if ((string.IsNullOrWhiteSpace(dto.Phone) && string.IsNullOrWhiteSpace(dto.Email)) || 
                string.IsNullOrWhiteSpace(dto.OtpCode) || 
                string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                return BadRequest(ApiResponse.Fail("Email or phone, OTP code, and new password are required."));
            }

            if (dto.NewPassword.Length < 6)
                return BadRequest(ApiResponse.Fail("New password must be at least 6 characters long."));

            // Atomic verification strictly enforces:
            // 1. One-time usage (is_verified = 0 atomically flipped to 1)
            // 2. Unexpired OTP (expires_at >= GETUTCDATE())
            // 3. Exact purpose ("FORGOT_PASSWORD")
            // 4. Prevention of reuse
            var isVerified = await _authDataService.VerifyOtpAsync(null, dto.Email, dto.Phone, dto.OtpCode, "FORGOT_PASSWORD");

            if (!isVerified)
                return BadRequest(ApiResponse.Fail("Invalid or expired OTP verification code."));

            var newHash = _passwordHasher.HashPassword(dto.NewPassword);
            var updated = await _authDataService.UpdatePasswordAsync(dto.Phone, dto.Email, newHash);
            if (!updated)
                return BadRequest(ApiResponse.Fail("Failed to update password. User account not found."));

            // Revoke all prior active sessions for this account upon password reset
            var user = await _authDataService.FindUserAsync(null, null, dto.Email, dto.Phone);
            if (user != null)
            {
                await _authDataService.InvalidateAllUserTokensAsync(user.UserId);
            }

            _logger.LogInformation("Password reset successfully for user: {Email}/{Phone}", dto.Email, dto.Phone);
            return Ok(ApiResponse.Ok("Password reset successfully. Please log in with your new password."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ResetPassword for user: {Email}/{Phone}", dto.Email, dto.Phone);
            return StatusCode(500, ApiResponse.Fail("Password reset failed."));
        }
    }

    /// <summary>
    /// Get current authenticated user profile.
    /// </summary>
    [HttpGet("profile")]
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

    /// <summary>
    /// Update current authenticated user profile.
    /// </summary>
    [HttpPut("profile")]
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

    /// <summary>
    /// Update current authenticated user dietary preference.
    /// </summary>
    [HttpPut("diet")]
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

    /// <summary>
    /// Verify active token validity. Protected by Bearer Token.
    /// </summary>
    [HttpPost("verify-token")]
    public async Task<IActionResult> VerifyToken([FromBody] VerifyTokenDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Token))
                return BadRequest(ApiResponse.Fail("Token is required."));

            var tokenRecord = await _authDataService.GetTokenAsync(dto.Token);
            if (tokenRecord == null)
                return Ok(new { valid = false, message = "Invalid or revoked token." });

            if (tokenRecord.ExpiresAt < DateTime.UtcNow)
                return Ok(new { valid = false, message = "Token expired." });

            var user = await _authDataService.GetUserByIdAsync(tokenRecord.UserId);
            if (user == null)
                return Ok(new { valid = false, message = "User not found." });

            return Ok(new
            {
                valid = true,
                user = new
                {
                    id = user.UserId,
                    phone = user.PhoneNumber,
                    email = user.Email,
                    username = user.Username,
                    fullName = user.FullName,
                    dietType = user.DietType,
                    isBiometricEnabled = user.IsBiometricEnabled,
                    createdAt = user.CreatedAt,
                    lastLoginAt = user.LastLoginAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in VerifyToken");
            return StatusCode(500, ApiResponse.Fail("Failed to verify token."));
        }
    }

    /// <summary>
    /// Logout and revoke current Bearer session in database.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var authHeader = Request.Headers.Authorization.ToString();
            var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) 
                ? authHeader[7..].Trim()
                : null;

            if (!string.IsNullOrEmpty(token))
            {
                await _authDataService.InvalidateTokenAsync(token);
                _logger.LogInformation("User logged out, token revoked in database");
            }

            return Ok(ApiResponse.Ok("Logged out successfully. Session revoked."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in Logout");
            return StatusCode(500, ApiResponse.Fail("Logout encountered an error."));
        }
    }
}
