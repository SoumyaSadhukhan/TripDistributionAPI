using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TripDistribution.Model.Settings;

namespace TripDistribution.Api.Services;

public interface IPhoneVerificationService
{
    Task<(bool IsValid, string? ErrorMessage, string? FormattedE164, string? Carrier)> VerifyPhoneAsync(string phone, string? defaultCountry = "IN");
}

public class VeriphoneApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("phone_valid")]
    public bool PhoneValid { get; set; }

    [JsonPropertyName("phone_type")]
    public string? PhoneType { get; set; }

    [JsonPropertyName("phone_region")]
    public string? PhoneRegion { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("carrier")]
    public string? Carrier { get; set; }

    [JsonPropertyName("e164")]
    public string? E164 { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class PhoneVerificationService : IPhoneVerificationService
{
    private readonly VerifyPhoneSettings _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhoneVerificationService> _logger;
    private readonly HttpClient _httpClient;

    public PhoneVerificationService(
        IConfiguration configuration,
        ILogger<PhoneVerificationService> logger,
        HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();

        _settings = new VerifyPhoneSettings();
        configuration.GetSection("VERIFY_PHONE").Bind(_settings);

        // Allow environment variable overrides
        var envKey = Environment.GetEnvironmentVariable("VERIFY_PHONE__API_KEY") 
                  ?? Environment.GetEnvironmentVariable("VERIFY_PHONE_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            _settings.API_KEY = envKey;
        }

        var envUrl = Environment.GetEnvironmentVariable("VERIFY_PHONE__API_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            _settings.API_URL = envUrl;
        }
    }

    public async Task<(bool IsValid, string? ErrorMessage, string? FormattedE164, string? Carrier)> VerifyPhoneAsync(string phone, string? defaultCountry = "IN")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return (false, "Phone number is required.", null, null);
            }

            var appEnv = (_configuration["Environment"] ?? "DEBUG").Trim().ToUpperInvariant();
            var isDebug = appEnv == "DEBUG";

            // In DEBUG mode or if API_KEY is placeholder / empty: allow any phone number to be entered
            if (isDebug || string.IsNullOrWhiteSpace(_settings.API_KEY) || _settings.API_KEY == "..." || _settings.API_KEY.Contains("CONFIGURED_VIA"))
            {
                _logger.LogInformation("[DEBUG PHONE VERIFY] Bypass active for phone: {Phone}. Accepting as valid without consuming API quota.", phone);
                Console.WriteLine($"[DEBUG PHONE VERIFY] Bypassed verification for: {phone} (Debug mode / Key not configured)");
                return (true, null, phone.StartsWith("+") ? phone : $"+91{phone.TrimStart('0')}", "Debug Carrier");
            }

            // PRODUCTION Mode: Invoke Veriphone API (Single call per registration)
            var apiUrl = !string.IsNullOrWhiteSpace(_settings.API_URL) 
                ? _settings.API_URL 
                : "https://api.veriphone.io/v2/verify";

            var requestUrl = $"{apiUrl}?key={Uri.EscapeDataString(_settings.API_KEY)}&phone={Uri.EscapeDataString(phone)}";
            if (!string.IsNullOrWhiteSpace(defaultCountry))
            {
                requestUrl += $"&default_country={Uri.EscapeDataString(defaultCountry)}";
            }

            _logger.LogInformation("Calling Veriphone API to verify phone number: {Phone}", phone);
            var response = await _httpClient.GetAsync(requestUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Veriphone API returned HTTP {StatusCode}", response.StatusCode);
                // Fail-safe: if Veriphone service is temporarily down or rate limited (HTTP 429), allow registration rather than hard blocking users
                return (true, null, phone, "Unknown Carrier");
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<VeriphoneApiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return (false, "Failed to parse phone verification response.", null, null);
            }

            if (!result.PhoneValid)
            {
                _logger.LogWarning("Phone validation failed for {Phone}. Status: {Status}, Error: {Error}", phone, result.Status, result.Error);
                return (false, "The provided phone number is not a valid mobile number.", null, null);
            }

            _logger.LogInformation("Phone verification succeeded for {Phone}. Carrier: {Carrier}, E164: {E164}", phone, result.Carrier, result.E164);
            return (true, null, result.E164 ?? phone, result.Carrier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception verifying phone number with Veriphone API");
            // If unexpected network failure occurs, fallback gracefully
            return (true, null, phone, "Fallback Carrier");
        }
    }
}
