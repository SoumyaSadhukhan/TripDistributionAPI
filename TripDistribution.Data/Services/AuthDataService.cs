using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Auth;
using TripDistribution.Model.Entities;

namespace TripDistribution.Data.Services;

public interface IAuthDataService
{
    Task<User?> GetUserByPhoneAsync(string phone);
    Task<User?> GetUserByIdAsync(string userId);
    Task<User?> FindUserAsync(string? userId, string? username, string? email, string? phone);
    Task<bool> SaveOtpAsync(string phone, string otpCode, string purpose, int expiryMinutes = 10);
    Task<bool> SaveOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose, int expiryMinutes = 10);
    Task<bool> VerifyOtpAsync(string phone, string otpCode, string purpose);
    Task<bool> VerifyOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose);
    Task<User> CreateUserAsync(RegisterRequestDto dto, string passwordHash);
    Task SaveUserTokenAsync(UserToken token);
    Task<UserToken?> GetTokenAsync(string token);
    Task InvalidateTokenAsync(string token);
    Task InvalidateAllUserTokensAsync(string userId);
    Task<bool> UpdatePasswordAsync(string phone, string newPasswordHash);
    Task<bool> UpdatePasswordAsync(string? phone, string? email, string newPasswordHash);
    Task UpdateLastLoginAsync(string userId);
}

public class AuthDataService : IAuthDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<AuthDataService> _logger;

    public AuthDataService(IDbConnectionFactory connectionFactory, ILogger<AuthDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<User?> GetUserByPhoneAsync(string phone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT user_id AS UserId, phone_number AS PhoneNumber, email AS Email, username AS Username, full_name AS FullName, 
                       password_hash AS PasswordHash, auth_token AS AuthToken, token_expiry AS TokenExpiry,
                       diet_type AS DietType, diet_name AS DietName, avatar_color AS AvatarColor,
                       is_biometric_enabled AS IsBiometricEnabled, is_active AS IsActive,
                       created_at AS CreatedAt, updated_at AS UpdatedAt, last_login_at AS LastLoginAt
                FROM Users 
                WHERE phone_number = @Phone";
            return await conn.QuerySingleOrDefaultAsync<User>(sql, new { Phone = phone });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by phone: {Phone}", phone);
            throw;
        }
    }

    public async Task<User?> GetUserByIdAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT user_id AS UserId, phone_number AS PhoneNumber, email AS Email, username AS Username, full_name AS FullName, 
                       password_hash AS PasswordHash, auth_token AS AuthToken, token_expiry AS TokenExpiry,
                       diet_type AS DietType, diet_name AS DietName, avatar_color AS AvatarColor,
                       is_biometric_enabled AS IsBiometricEnabled, is_active AS IsActive,
                       created_at AS CreatedAt, updated_at AS UpdatedAt, last_login_at AS LastLoginAt
                FROM Users 
                WHERE user_id = @UserId";
            return await conn.QuerySingleOrDefaultAsync<User>(sql, new { UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by ID: {UserId}", userId);
            throw;
        }
    }

    public async Task<User?> FindUserAsync(string? userId, string? username, string? email, string? phone)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT TOP 1 user_id AS UserId, phone_number AS PhoneNumber, email AS Email, username AS Username, full_name AS FullName, 
                       password_hash AS PasswordHash, auth_token AS AuthToken, token_expiry AS TokenExpiry,
                       diet_type AS DietType, diet_name AS DietName, avatar_color AS AvatarColor,
                       is_biometric_enabled AS IsBiometricEnabled, is_active AS IsActive,
                       created_at AS CreatedAt, updated_at AS UpdatedAt, last_login_at AS LastLoginAt
                FROM Users 
                WHERE (@UserId IS NOT NULL AND @UserId <> '' AND user_id = @UserId)
                   OR (@Username IS NOT NULL AND @Username <> '' AND username = @Username)
                   OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email)
                   OR (@Phone IS NOT NULL AND @Phone <> '' AND phone_number = @Phone)";
            return await conn.QuerySingleOrDefaultAsync<User>(sql, new { UserId = userId, Username = username, Email = email, Phone = phone });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding user by identifier");
            throw;
        }
    }

    public Task<bool> SaveOtpAsync(string phone, string otpCode, string purpose, int expiryMinutes = 10)
        => SaveOtpAsync(null, null, phone, otpCode, purpose, expiryMinutes);

    public async Task<bool> SaveOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose, int expiryMinutes = 10)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            
            // Invalidate old OTPs for matching destination
            const string invalidateSql = @"
                UPDATE OtpVerification 
                SET is_verified = 1 
                WHERE is_verified = 0 
                  AND purpose = @Purpose 
                  AND ((@UserId IS NOT NULL AND @UserId <> '' AND user_id = @UserId)
                    OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email)
                    OR (@Phone IS NOT NULL AND @Phone <> '' AND @Phone <> 'EMAIL_ONLY' AND phone_number = @Phone))";
            await conn.ExecuteAsync(invalidateSql, new { UserId = userId, Email = email, Phone = phone, Purpose = purpose });

            const string sql = @"
                INSERT INTO OtpVerification (otp_id, user_id, phone_number, email, otp_code, purpose, is_verified, expires_at, created_at)
                VALUES (@OtpId, @UserId, @Phone, @Email, @OtpCode, @Purpose, 0, @ExpiresAt, GETDATE())";

            string dbPhone = !string.IsNullOrWhiteSpace(phone)
                ? (phone.Length > 20 ? phone.Substring(0, 20) : phone)
                : "EMAIL_ONLY";

            var rows = await conn.ExecuteAsync(sql, new
            {
                OtpId = Guid.NewGuid().ToString(),
                UserId = userId,
                Phone = dbPhone,
                Email = email,
                OtpCode = otpCode,
                Purpose = purpose,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes)
            });

            _logger.LogInformation("Saved OTP for user: {UserId}, email: {Email}, phone: {Phone}, purpose: {Purpose}", userId, email, phone, purpose);
            Console.WriteLine($"OTP for {(email ?? phone ?? userId)} is: {otpCode}");
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OTP");
            throw;
        }
    }

    public Task<bool> VerifyOtpAsync(string phone, string otpCode, string purpose)
        => VerifyOtpAsync(null, null, phone, otpCode, purpose);

    public async Task<bool> VerifyOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            
            // Atomic CTE update with UPDLOCK/ROWLOCK prevents TOCTOU race conditions and concurrent double-consumption
            const string sql = @"
                WITH TargetOtp AS (
                    SELECT TOP 1 otp_id, user_id, is_verified
                    FROM OtpVerification WITH (UPDLOCK, ROWLOCK)
                    WHERE otp_code = @OtpCode 
                      AND purpose = @Purpose 
                      AND is_verified = 0 
                      AND expires_at >= GETUTCDATE()
                      AND ((@UserId IS NOT NULL AND @UserId <> '' AND user_id = @UserId)
                        OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email)
                        OR (@Phone IS NOT NULL AND @Phone <> '' AND @Phone <> 'EMAIL_ONLY' AND phone_number = @Phone))
                    ORDER BY created_at DESC
                )
                UPDATE TargetOtp
                SET is_verified = 1
                OUTPUT inserted.otp_id, inserted.user_id;";

            var result = await conn.QuerySingleOrDefaultAsync<dynamic>(sql, new { UserId = userId, Email = email, Phone = phone, OtpCode = otpCode, Purpose = purpose });
            if (result == null)
            {
                _logger.LogWarning("OTP verification failed or already consumed for user: {UserId}, email: {Email}, phone: {Phone}", userId, email, phone);
                return false;
            }

            string? matchedUserId = result.user_id;

            // Activate user if associated with user_id
            var targetUid = !string.IsNullOrEmpty(matchedUserId) ? matchedUserId : userId;
            if (!string.IsNullOrEmpty(targetUid))
            {
                await conn.ExecuteAsync("UPDATE Users SET is_active = 1, updated_at = GETDATE() WHERE user_id = @UserId", new { UserId = targetUid });
            }

            _logger.LogInformation("OTP verified and atomically consumed for user: {UserId}, email: {Email}, phone: {Phone}", userId, email, phone);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying OTP");
            throw;
        }
    }

    public async Task<User> CreateUserAsync(RegisterRequestDto dto, string passwordHash)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var userId = Guid.NewGuid().ToString();
            var dietName = !string.IsNullOrWhiteSpace(dto.DietName) ? dto.DietName : dto.DietType.ToString();
            var avatarColor = !string.IsNullOrWhiteSpace(dto.AvatarColor) ? dto.AvatarColor : "#4F46E5";
            var phone = !string.IsNullOrWhiteSpace(dto.Phone) ? dto.Phone : "N/A";
            var username = !string.IsNullOrWhiteSpace(dto.Username) 
                ? dto.Username 
                : (!string.IsNullOrWhiteSpace(dto.Email) ? dto.Email.Split('@')[0] : phone);

            const string sql = @"
                INSERT INTO Users (user_id, phone_number, email, username, full_name, password_hash, diet_type, diet_name, avatar_color, is_biometric_enabled, is_active, created_at, updated_at)
                VALUES (@UserId, @PhoneNumber, @Email, @Username, @FullName, @PasswordHash, @DietType, @DietName, @AvatarColor, 0, 1, GETDATE(), GETDATE())";

            await conn.ExecuteAsync(sql, new
            {
                UserId = userId,
                PhoneNumber = phone,
                Email = dto.Email,
                Username = username,
                FullName = dto.FullName,
                PasswordHash = passwordHash,
                DietType = (int)dto.DietType,
                DietName = dietName,
                AvatarColor = avatarColor
            });

            _logger.LogInformation("Created new user: {UserId}, Username: {Username}, Email: {Email}, Phone: {Phone}", userId, username, dto.Email, phone);
            return (await GetUserByIdAsync(userId))!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user for identifier: {Phone}", dto.Phone);
            throw;
        }
    }

    public async Task SaveUserTokenAsync(UserToken token)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO UserTokens (token_id, user_id, phone_number, auth_token, device_info, is_active, expires_at, created_at, last_used_at)
                VALUES (@TokenId, @UserId, @PhoneNumber, @AuthToken, @DeviceInfo, @IsActive, @ExpiresAt, GETDATE(), GETDATE())";

            await conn.ExecuteAsync(sql, token);
            _logger.LogInformation("Saved auth token for user: {UserId}", token.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving auth token for user: {UserId}", token.UserId);
            throw;
        }
    }

    public async Task<UserToken?> GetTokenAsync(string token)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT token_id AS TokenId, user_id AS UserId, phone_number AS PhoneNumber,
                       auth_token AS AuthToken, device_info AS DeviceInfo, is_active AS IsActive,
                       expires_at AS ExpiresAt, created_at AS CreatedAt, last_used_at AS LastUsedAt
                FROM UserTokens 
                WHERE auth_token = @AuthToken AND is_active = 1";

            return await conn.QuerySingleOrDefaultAsync<UserToken>(sql, new { AuthToken = token });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching auth token record");
            throw;
        }
    }

    public async Task InvalidateTokenAsync(string token)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            await conn.ExecuteAsync("UPDATE UserTokens SET is_active = 0 WHERE auth_token = @AuthToken", new { AuthToken = token });
            _logger.LogInformation("Token invalidated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating token");
            throw;
        }
    }

    public async Task InvalidateAllUserTokensAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            await conn.ExecuteAsync("UPDATE UserTokens SET is_active = 0 WHERE user_id = @UserId", new { UserId = userId });
            _logger.LogInformation("All tokens invalidated for user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating all tokens for user: {UserId}", userId);
            throw;
        }
    }

    public Task<bool> UpdatePasswordAsync(string phone, string newPasswordHash)
        => UpdatePasswordAsync(phone, null, newPasswordHash);

    public async Task<bool> UpdatePasswordAsync(string? phone, string? email, string newPasswordHash)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var rows = await conn.ExecuteAsync(
                @"UPDATE Users SET password_hash = @PasswordHash, updated_at = GETDATE() 
                  WHERE is_active = 1 
                    AND ((@Phone IS NOT NULL AND @Phone <> '' AND phone_number = @Phone)
                      OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email))",
                new { PasswordHash = newPasswordHash, Phone = phone, Email = email });

            _logger.LogInformation("Password updated for phone: {Phone}, email: {Email}", phone, email);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password for phone: {Phone}, email: {Email}", phone, email);
            throw;
        }
    }

    public async Task UpdateLastLoginAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            await conn.ExecuteAsync("UPDATE Users SET last_login_at = GETDATE() WHERE user_id = @UserId", new { UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last login for user: {UserId}", userId);
            throw;
        }
    }
}
