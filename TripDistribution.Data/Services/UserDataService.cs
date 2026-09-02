using Dapper;
using Microsoft.Extensions.Logging;
using TripDistribution.Data.Connection;
using TripDistribution.Model.DTOs.Auth;
using TripDistribution.Model.Entities;
using TripDistribution.Model.Enums;

namespace TripDistribution.Data.Services;

public interface IUserDataService
{
    Task<UserProfileDto?> GetProfileAsync(string userId);
    Task<bool> UpdateDietAsync(string userId, DietType dietType, string? dietName);
    Task<bool> UpdateProfileAsync(string userId, UpdateProfileDto dto);
    Task<bool> ToggleBiometricAsync(string userId, bool enabled);
    Task<IEnumerable<UserProfileDto>> SearchUsersAsync(string query);
}

public class UserDataService : IUserDataService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<UserDataService> _logger;

    public UserDataService(IDbConnectionFactory connectionFactory, ILogger<UserDataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<UserProfileDto?> GetProfileAsync(string userId)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT user_id AS UserId, phone_number AS Phone, full_name AS FullName,
                       diet_type AS DietType, diet_name AS DietName, avatar_color AS AvatarColor,
                       is_biometric_enabled AS IsBiometricEnabled, created_at AS CreatedAt,
                       last_login_at AS LastLoginAt
                FROM Users
                WHERE user_id = @UserId AND is_active = 1";

            return await conn.QuerySingleOrDefaultAsync<UserProfileDto>(sql, new { UserId = userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching profile for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> UpdateDietAsync(string userId, DietType dietType, string? dietName)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var name = !string.IsNullOrWhiteSpace(dietName) ? dietName : dietType.ToString();
            const string sql = @"
                UPDATE Users 
                SET diet_type = @DietType, diet_name = @DietName, updated_at = GETDATE()
                WHERE user_id = @UserId AND is_active = 1";

            var rows = await conn.ExecuteAsync(sql, new { UserId = userId, DietType = (int)dietType, DietName = name });
            _logger.LogInformation("Diet preference updated for user {UserId} to {DietType}", userId, dietType);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating diet for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> UpdateProfileAsync(string userId, UpdateProfileDto dto)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                UPDATE Users 
                SET full_name = COALESCE(@FullName, full_name),
                    avatar_color = COALESCE(@AvatarColor, avatar_color),
                    is_biometric_enabled = COALESCE(@IsBiometricEnabled, is_biometric_enabled),
                    updated_at = GETDATE()
                WHERE user_id = @UserId AND is_active = 1";

            var rows = await conn.ExecuteAsync(sql, new
            {
                UserId = userId,
                dto.FullName,
                dto.AvatarColor,
                dto.IsBiometricEnabled
            });
            _logger.LogInformation("Profile updated for user: {UserId}", userId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> ToggleBiometricAsync(string userId, bool enabled)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = "UPDATE Users SET is_biometric_enabled = @Enabled, updated_at = GETDATE() WHERE user_id = @UserId";
            var rows = await conn.ExecuteAsync(sql, new { UserId = userId, Enabled = enabled });
            _logger.LogInformation("Biometric state toggled for user: {UserId} to {Enabled}", userId, enabled);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling biometric for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<UserProfileDto>> SearchUsersAsync(string query)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var clean = query.Trim();
            const string sql = @"
                SELECT TOP 20 user_id AS UserId, phone_number AS Phone, full_name AS FullName,
                       diet_type AS DietType, diet_name AS DietName, avatar_color AS AvatarColor,
                       is_biometric_enabled AS IsBiometricEnabled, created_at AS CreatedAt,
                       last_login_at AS LastLoginAt
                FROM Users
                WHERE (phone_number LIKE @Search OR full_name LIKE @Search) AND is_active = 1";

            return await conn.QueryAsync<UserProfileDto>(sql, new { Search = $"%{clean}%" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users with query: {Query}", query);
            throw;
        }
    }
}
