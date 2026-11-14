using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TripDistribution.Api.Middleware;
using TripDistribution.Api.Services;
using TripDistribution.Data.Connection;
using TripDistribution.Data.Services;
using TripDistribution.Model.DTOs.Common;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Cloud hosting dynamic port binding (Render, Railway, Fly.io, Cloud Run)
var cloudPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(cloudPort))
{
    builder.WebHost.UseUrls($"http://*:{cloudPort}");
}

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/api_log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 1. Controllers & JSON Serialization
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

// 2. Swagger with JWT Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Trip Distribution API",
        Version = "v1",
        Description = "Enterprise Production REST API for Trip Splitting, Expense Distribution, and Debt Settlements"
    });

    // JWT Bearer Security Definition (OpenAPI Standard)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter your JWT token. Swagger will automatically prefix 'Bearer '.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 3. Database Connection Factory
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// 4. DataServices (Scoped)
builder.Services.AddScoped<IAuthDataService, AuthDataService>();
builder.Services.AddScoped<IUserDataService, UserDataService>();
builder.Services.AddScoped<ITripDataService, TripDataService>();
builder.Services.AddScoped<IGroupDataService, GroupDataService>();
builder.Services.AddScoped<IPersonDataService, PersonDataService>();
builder.Services.AddScoped<IExpenseDataService, ExpenseDataService>();
builder.Services.AddScoped<ISettlementDataService, SettlementDataService>();
builder.Services.AddScoped<IFriendDataService, FriendDataService>();
builder.Services.AddScoped<ICollaboratorDataService, CollaboratorDataService>();
builder.Services.AddScoped<INotificationDataService, NotificationDataService>();
builder.Services.AddScoped<IActivityLogDataService, ActivityLogDataService>();

// 5. Auth & User Services
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IEmailService, EmailService>();

// 6. JWT Authentication Configuration with DB-Backed Session Validation
var jwtKey = builder.Configuration["JwtSettings:SecretKey"] 
    ?? "SplitBillSuperSecretKey_ForTripDistribution_ProductionReady_2026_Key_MustBeLongEnough!";
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "TripDistribution.Api";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "TripDistribution.Client";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    // Database-backed Token Validation & Revocation Check
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var authDataService = context.HttpContext.RequestServices.GetRequiredService<IAuthDataService>();

            // Extract raw token string
            var authHeader = context.Request.Headers.Authorization.ToString();
            var rawToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader[7..].Trim()
                : null;

            if (string.IsNullOrEmpty(rawToken))
            {
                context.Fail("Bearer token missing.");
                return;
            }

            // Query database UserTokens table: must exist, be active, and not expired
            var tokenRecord = await authDataService.GetTokenAsync(rawToken);
            if (tokenRecord == null || !tokenRecord.IsActive || tokenRecord.ExpiresAt < DateTime.UtcNow)
            {
                context.Fail("Session has been revoked or expired. Please log in again.");
                return;
            }

            // Verify authenticated user ID matches token owner
            var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.Principal?.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(userId) || tokenRecord.UserId != userId)
            {
                context.Fail("Token does not match authenticated user.");
                return;
            }
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var response = ApiResponse.Fail("Unauthorized. A valid Bearer token is required to access this resource.");
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    };
});

builder.Services.AddAuthorization();

// 7. Rate Limiter (Protection against Brute-Force & Denial of Service)
var permitLimit = builder.Configuration.GetValue<int>("RateLimiting:PermitLimit", builder.Environment.IsDevelopment() ? 2000 : 30);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth-policy", opt =>
    {
        opt.PermitLimit = permitLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 100;
    });
});

// 8. CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Middleware Pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

// Enable Swagger in Development and Production for testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trip Distribution API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger at app root URL
});

app.UseCors("AllowAll");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
