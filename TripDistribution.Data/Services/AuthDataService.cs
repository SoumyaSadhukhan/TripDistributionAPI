using System.Linq;
using Dapper;
using Microsoft.Extensions.Configuration;
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
    Task<bool> SaveOtpAsync(string phone, string otpCode, string purpose, int expiryMinutes = 10, string? ipAddress = null);
    Task<bool> SaveOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose, int expiryMinutes = 10, string? ipAddress = null);
    Task<bool> VerifyOtpAsync(string phone, string otpCode, string purpose, string? ipAddress = null);
    Task<bool> VerifyOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose, string? ipAddress = null);
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthDataService> _logger;

    public AuthDataService(IDbConnectionFactory connectionFactory, IConfiguration configuration, ILogger<AuthDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
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
            
            // 1. First, search directly with the provided identifiers (including exact country code + phone)
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
            var user = await conn.QuerySingleOrDefaultAsync<User>(sql, new { UserId = userId, Username = username, Email = email, Phone = phone });
            if (user != null) return user;

            // 2. Secondary fallback search by phone without country code or ending digits if not found initially
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var cleanDigits = new string(phone.Where(char.IsDigit).ToArray());
                var rawPhone = cleanDigits.Length >= 10 
                    ? cleanDigits.Substring(cleanDigits.Length - 10) 
                    : cleanDigits;

                if (!string.IsNullOrEmpty(rawPhone))
                {
                    const string fallbackSql = @"
                        SELECT TOP 1 user_id AS UserId, phone_number AS PhoneNumber, email AS Email, username AS Username, full_name AS FullName, 
                               password_hash AS PasswordHash, auth_token AS AuthToken, token_expiry AS TokenExpiry,
                               diet_type AS DietType, diet_name AS DietName, avatar_color AS AvatarColor,
                               is_biometric_enabled AS IsBiometricEnabled, is_active AS IsActive,
                               created_at AS CreatedAt, updated_at AS UpdatedAt, last_login_at AS LastLoginAt
                        FROM Users 
                        WHERE phone_number = @RawPhone
                           OR phone_number LIKE '%' + @RawPhone";
                    user = await conn.QuerySingleOrDefaultAsync<User>(fallbackSql, new { RawPhone = rawPhone });
                    if (user != null) return user;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding user by identifier");
            throw;
        }
    }

    private string GetOtpSecretKey()
    {
        return _configuration["AuthSettings:OtpSecretKey"] 
            ?? Environment.GetEnvironmentVariable("EQUITRIP_OTP_SECRET_KEY")
            ?? "EquiTrip_Production_Secure_Hmac_Otp_Key_2026_!@#$%^&*";
    }

    private string? GetPreviousOtpSecretKey()
    {
        return _configuration["AuthSettings:OtpPreviousSecretKey"]
            ?? Environment.GetEnvironmentVariable("EQUITRIP_OTP_PREVIOUS_SECRET_KEY");
    }

    private static bool FixedTimeHexEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            var bytesA = Convert.FromHexString(a.Trim());
            var bytesB = Convert.FromHexString(b.Trim());
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeOtpHmac(string otpCode, string destination, string purpose, string secretKey)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
        var message = $"{purpose.ToUpperInvariant()}:{destination.Trim().ToLowerInvariant()}:{otpCode.Trim()}";
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash);
    }

    public Task<bool> SaveOtpAsync(string phone, string otpCode, string purpose, int expiryMinutes = 10, string? ipAddress = null)
        => SaveOtpAsync(null, null, phone, otpCode, purpose, expiryMinutes, ipAddress);

    public async Task<bool> SaveOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose, int expiryMinutes = 10, string? ipAddress = null)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            
            string dbPhone = !string.IsNullOrWhiteSpace(phone)
                ? (phone.Length > 20 ? phone.Substring(0, 20) : phone)
                : "EMAIL_ONLY";

            var destination = email ?? (!string.IsNullOrWhiteSpace(phone) ? dbPhone : (userId ?? "DEFAULT"));

            // 1. IP Rate Limit: Max 10 OTP requests per hour per IP
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                const string ipCheckSql = @"
                    SELECT COUNT(1) 
                    FROM OtpVerification
                    WHERE ip_address = @IpAddress 
                      AND created_at >= DATEADD(hour, -1, GETUTCDATE());";
                var ipCount = await conn.ExecuteScalarAsync<int>(ipCheckSql, new { IpAddress = ipAddress });
                if (ipCount >= 10)
                {
                    _logger.LogWarning("IP rate limit exceeded for {IpAddress}: {Count} requests in last hour", ipAddress, ipCount);
                    throw new InvalidOperationException("Too many OTP requests from this IP address. Please try again later.");
                }
            }

            // 2. Account Resend Rate Limit (60-second cooldown on active unverified OTPs)
            const string rateLimitCheckSql = @"
                SELECT COUNT(1) 
                FROM OtpVerification
                WHERE purpose = @Purpose 
                  AND is_verified = 0 
                  AND created_at >= DATEADD(second, -60, GETDATE())
                  AND ((@UserId IS NOT NULL AND @UserId <> '' AND user_id = @UserId)
                    OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email)
                    OR (@Phone IS NOT NULL AND @Phone <> '' AND @Phone <> 'EMAIL_ONLY' AND phone_number = @Phone));";

            var recentCount = await conn.ExecuteScalarAsync<int>(rateLimitCheckSql, new { UserId = userId, Email = email, Phone = dbPhone, Purpose = purpose });
            if (recentCount > 0)
            {
                _logger.LogWarning("OTP resend rate limit triggered for Destination: {Destination}, Purpose: {Purpose}", destination, purpose);
                throw new InvalidOperationException("Please wait 60 seconds before requesting another OTP verification code.");
            }

            // 3. Invalidate prior active OTPs for matching destination & purpose
            const string invalidateSql = @"
                UPDATE OtpVerification 
                SET is_verified = 1 
                WHERE is_verified = 0 
                  AND purpose = @Purpose 
                  AND ((@UserId IS NOT NULL AND @UserId <> '' AND user_id = @UserId)
                    OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email)
                    OR (@Phone IS NOT NULL AND @Phone <> '' AND @Phone <> 'EMAIL_ONLY' AND phone_number = @Phone))";
            await conn.ExecuteAsync(invalidateSql, new { UserId = userId, Email = email, Phone = dbPhone, Purpose = purpose });

            // 4. Compute Keyed Cryptographic HMAC-SHA256
            var secretKey = GetOtpSecretKey();
            var otpHmac = ComputeOtpHmac(otpCode, destination, purpose, secretKey);

            // 5. Insert strictly with HMAC hash, redacted plaintext, and IP tracking
            const string sql = @"
                INSERT INTO OtpVerification (otp_id, user_id, phone_number, email, otp_code, otp_hash, purpose, is_verified, failed_attempts, max_attempts, lockout_until, ip_address, expires_at, created_at)
                VALUES (@OtpId, @UserId, @Phone, @Email, 'REDACTED', @OtpHmac, @Purpose, 0, 0, 5, NULL, @IpAddress, @ExpiresAt, GETDATE())";

            var rows = await conn.ExecuteAsync(sql, new
            {
                OtpId = Guid.NewGuid().ToString(),
                UserId = userId,
                Phone = dbPhone,
                Email = email,
                OtpHmac = otpHmac,
                Purpose = purpose,
                IpAddress = ipAddress,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes)
            });

            var isDebug = !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase) 
                       && !string.Equals(Environment.GetEnvironmentVariable("Environment"), "PRODUCTION", StringComparison.OrdinalIgnoreCase);

            if (isDebug)
            {
                _logger.LogInformation("[DEBUG OTP] User: {UserId}, Email: {Email}, Phone: {Phone}, Purpose: {Purpose}, OTP: {OtpCode}", userId, email, phone, purpose, otpCode);
                Console.WriteLine($"[DEBUG OTP] Destination: {destination} | OTP: {otpCode}");
            }
            else
            {
                _logger.LogInformation("Saved OTP HMAC hash for verification purpose: {Purpose}", purpose);
            }
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OTP");
            throw;
        }
    }

    public Task<bool> VerifyOtpAsync(string phone, string otpCode, string purpose, string? ipAddress = null)
        => VerifyOtpAsync(null, null, phone, otpCode, purpose, ipAddress);

    public async Task<bool> VerifyOtpAsync(string? userId, string? email, string? phone, string otpCode, string purpose, string? ipAddress = null)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            string dbPhone = !string.IsNullOrWhiteSpace(phone)
                ? (phone.Length > 20 ? phone.Substring(0, 20) : phone)
                : "EMAIL_ONLY";

            var destination = email ?? (!string.IsNullOrWhiteSpace(phone) ? dbPhone : (userId ?? "DEFAULT"));

            // 1. IP Brute Force Check: Max 15 failed verification attempts in last 15 minutes
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                const string ipFailCheckSql = @"
                    SELECT SUM(failed_attempts)
                    FROM OtpVerification
                    WHERE ip_address = @IpAddress 
                      AND created_at >= DATEADD(minute, -15, GETUTCDATE());";
                var totalFails = await conn.ExecuteScalarAsync<int?>(ipFailCheckSql, new { IpAddress = ipAddress }) ?? 0;
                if (totalFails >= 15)
                {
                    _logger.LogWarning("IP brute-force block triggered for {IpAddress}: {Count} failed attempts in last 15 mins", ipAddress, totalFails);
                    throw new InvalidOperationException("Too many failed verification attempts from this IP address. Please try again in 15 minutes.");
                }
            }

            // 2. Fetch active target OTP with row-level locking (UPDLOCK, ROWLOCK)
            const string selectSql = @"
                SELECT TOP 1 otp_id AS OtpId, user_id AS UserId, otp_code AS OtpCode, otp_hash AS OtpHash, 
                             purpose AS Purpose, is_verified AS IsVerified, failed_attempts AS FailedAttempts, 
                             max_attempts AS MaxAttempts, lockout_until AS LockoutUntil, expires_at AS ExpiresAt
                FROM OtpVerification WITH (UPDLOCK, ROWLOCK)
                WHERE purpose = @Purpose 
                  AND is_verified = 0 
                  AND expires_at >= GETUTCDATE()
                  AND ((@UserId IS NOT NULL AND @UserId <> '' AND user_id = @UserId)
                    OR (@Email IS NOT NULL AND @Email <> '' AND email = @Email)
                    OR (@Phone IS NOT NULL AND @Phone <> '' AND @Phone <> 'EMAIL_ONLY' AND phone_number = @Phone))
                ORDER BY created_at DESC;";

            var target = await conn.QuerySingleOrDefaultAsync<OtpVerification>(selectSql, new 
            { 
                UserId = userId, 
                Email = email, 
                Phone = dbPhone, 
                Purpose = purpose 
            });

            if (target == null)
            {
                _logger.LogWarning("OTP verification failed: no active, unexpired OTP found for destination: {Destination}, Purpose: {Purpose}", destination, purpose);
                return false;
            }

            // 3. Enforce Lockout: Check if currently locked out
            if (target.LockoutUntil.HasValue && target.LockoutUntil.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("OTP verification blocked: locked out until {LockoutUntil} for destination {Destination}", target.LockoutUntil.Value, destination);
                throw new InvalidOperationException($"Account is temporarily locked out until {target.LockoutUntil.Value:u} UTC due to excessive failed attempts. Please try again later.");
            }

            if (target.FailedAttempts >= target.MaxAttempts)
            {
                _logger.LogWarning("OTP locked out due to exceeding max failed attempts ({Attempts}/{Max}) for OtpId {OtpId}", target.FailedAttempts, target.MaxAttempts, target.OtpId);
                await conn.ExecuteAsync("UPDATE OtpVerification SET is_verified = 1, lockout_until = DATEADD(minute, 15, GETUTCDATE()) WHERE otp_id = @OtpId", new { OtpId = target.OtpId });
                throw new InvalidOperationException("Account is temporarily locked out due to excessive failed attempts. Please try again in 15 minutes.");
            }

            // 4. Keyed HMAC-SHA256 Validation with Rotation Support
            var primaryKey = GetOtpSecretKey();
            var expectedHmac = ComputeOtpHmac(otpCode, destination, purpose, primaryKey);

            bool isMatch = FixedTimeHexEquals(target.OtpHash, expectedHmac);

            if (!isMatch)
            {
                var prevKey = GetPreviousOtpSecretKey();
                if (!string.IsNullOrEmpty(prevKey))
                {
                    var fallbackHmac = ComputeOtpHmac(otpCode, destination, purpose, prevKey);
                    isMatch = FixedTimeHexEquals(target.OtpHash, fallbackHmac);
                }
            }

            if (!isMatch)
            {
                // Increment failed attempts and set lockout_until if threshold reached
                const string failSql = @"
                    UPDATE OtpVerification 
                    SET failed_attempts = failed_attempts + 1,
                        is_verified = CASE WHEN failed_attempts + 1 >= max_attempts THEN 1 ELSE 0 END,
                        lockout_until = CASE WHEN failed_attempts + 1 >= max_attempts THEN DATEADD(minute, 15, GETUTCDATE()) ELSE NULL END
                    WHERE otp_id = @OtpId;";

                await conn.ExecuteAsync(failSql, new { OtpId = target.OtpId });
                _logger.LogWarning("Incorrect OTP attempt for OtpId: {OtpId}. Failed attempts incremented.", target.OtpId);

                if (target.FailedAttempts + 1 >= target.MaxAttempts)
                {
                    throw new InvalidOperationException("Maximum verification attempts exceeded. Account is locked out for 15 minutes.");
                }

                return false;
            }

            // 5. Mark atomically consumed (is_verified = 1, lockout_until = NULL)
            const string updateSuccessSql = @"
                UPDATE OtpVerification
                SET is_verified = 1, failed_attempts = 0, lockout_until = NULL
                WHERE otp_id = @OtpId AND is_verified = 0;";

            var updatedRows = await conn.ExecuteAsync(updateSuccessSql, new { OtpId = target.OtpId });
            if (updatedRows == 0)
            {
                _logger.LogWarning("Race condition detected: OTP {OtpId} was already consumed concurrently.", target.OtpId);
                return false;
            }

            // Activate user if associated with user_id
            var targetUid = !string.IsNullOrEmpty(target.UserId) ? target.UserId : userId;
            if (!string.IsNullOrEmpty(targetUid))
            {
                await conn.ExecuteAsync("UPDATE Users SET is_active = 1, updated_at = GETDATE() WHERE user_id = @UserId", new { UserId = targetUid });
            }

            _logger.LogInformation("OTP verified and atomically consumed for user: {UserId}, email: {Email}, phone: {Phone}", userId, email, phone);
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
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
