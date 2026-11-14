using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace TripDistribution.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var correlationId = Guid.NewGuid().ToString();
                _logger.LogError(ex, "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}", 
                    correlationId, context.Request.Path, context.Request.Method);
                await HandleExceptionAsync(context, ex, correlationId, _environment);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId, IHostEnvironment env)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                statusCode = context.Response.StatusCode,
                message = "An unexpected error occurred while processing your request.",
                correlationId = correlationId,
                detailed = env.IsDevelopment() ? exception.Message : null
            };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
        }
    }
}
