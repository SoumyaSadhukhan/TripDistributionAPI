using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TripDistribution.Api.Middleware;

public class EnvironmentLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvironmentLoggingMiddleware> _logger;

    public EnvironmentLoggingMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<EnvironmentLoggingMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var appEnv = (_configuration["Environment"] ?? "DEBUG").Trim().ToUpperInvariant();
        var isDebug = appEnv == "DEBUG";

        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";
        var queryString = context.Request.QueryString.Value ?? "";

        // Suppress noisy swagger asset requests
        var isSwaggerAsset = path.Contains("/swagger/") && !path.EndsWith("v1/swagger.json");

        if (isDebug)
        {
            // ===============================================================
            // #if DEBUG (Environment: DEBUG)
            // Log full request details: User info, route, and incoming DTO
            // ===============================================================
            context.Request.EnableBuffering();
            string requestBody = string.Empty;
            if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
            }

            var userId = context.User?.FindFirst("userId")?.Value 
                      ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? "Unauthenticated";
            var userName = context.User?.FindFirst("fullName")?.Value 
                        ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n================== [API CALL INITIALIZED - DEBUG] ==================");
            Console.WriteLine($"Endpoint     : {method} {path}{queryString}");
            Console.WriteLine($"User Details : UserID={userId}, Name={userName ?? "None"}");
            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                Console.WriteLine($"Incoming DTO : {requestBody}");
            }
            Console.WriteLine($"Timestamp    : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            Console.WriteLine($"====================================================================");
            Console.ResetColor();

            _logger.LogInformation("[DEBUG] API Call Initialized: {Method} {Path}{Query} | User: {UserId} | DTO: {RequestBody}", 
                method, path, queryString, userId, requestBody);

            // Intercept response stream to log outgoing response DTO
            var originalBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                await _next(context);

                responseBodyStream.Seek(0, SeekOrigin.Begin);
                var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();
                responseBodyStream.Seek(0, SeekOrigin.Begin);

                var statusCode = context.Response.StatusCode;
                var isSuccess = statusCode >= 200 && statusCode < 400;

                Console.ForegroundColor = isSuccess ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"\n================== [API CALL {(isSuccess ? "SUCCESS" : "ERROR")} - DEBUG] ==================");
                Console.WriteLine($"Endpoint     : {method} {path} (HTTP {statusCode})");
                Console.WriteLine($"Outgoing DTO : {responseBody}");
                Console.WriteLine($"Timestamp    : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine($"====================================================================\n");
                Console.ResetColor();

                _logger.LogInformation("[DEBUG] API Call {Status}: {Method} {Path} (HTTP {StatusCode}) | Response: {ResponseBody}",
                    isSuccess ? "Success" : "Error", method, path, statusCode, responseBody);

                await responseBodyStream.CopyToAsync(originalBodyStream);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[DEBUG API ERROR] {method} {path} -> {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                _logger.LogError(ex, "[DEBUG] API Call Exception: {Method} {Path}", method, path);
                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }
        else
        {
            // ===============================================================
            // #else (Environment: PRODUCTION)
            // Suppress all user details, OTP, and DTO payloads!
            // Only output: API call initialized, success, or error.
            // ===============================================================
            if (!isSwaggerAsset)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"API call initialized: {method} {path}");
                Console.ResetColor();
                _logger.LogInformation("API call initialized: {Method} {Path}", method, path);
            }

            try
            {
                await _next(context);

                var statusCode = context.Response.StatusCode;
                if (!isSwaggerAsset)
                {
                    if (statusCode >= 200 && statusCode < 400)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"API call success: {method} {path} (HTTP {statusCode})");
                        Console.ResetColor();
                        _logger.LogInformation("API call success: {Method} {Path} (HTTP {StatusCode})", method, path, statusCode);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"API call error: {method} {path} (HTTP {statusCode})");
                        Console.ResetColor();
                        _logger.LogError("API call error: {Method} {Path} (HTTP {StatusCode})", method, path, statusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"API call error: {method} {path} - {ex.Message}");
                Console.ResetColor();
                _logger.LogError(ex, "API call error: {Method} {Path} - {Message}", method, path, ex.Message);
                throw;
            }
        }
    }
}
