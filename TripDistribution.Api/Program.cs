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

// Enforce request body size limits in Kestrel (Protection against payload bombing)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB maximum request body size
});

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

// 6. JWT Authentication Configuration with Strict Secret Validation & DB Session Verification
var defaultFallbackKey = "SplitBillSuperSecretKey_ForTripDistribution_ProductionReady_2026_Key_MustBeLongEnough!";
var jwtKey = builder.Configuration["JwtSettings:SecretKey"] 
    ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY");

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == defaultFallbackKey)
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("CRITICAL: Production JWT SecretKey is missing or using default fallback! Configure JWT_SECRET_KEY in environment variables.");
    }
    jwtKey = defaultFallbackKey;
}
else if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("CRITICAL: JWT SecretKey must be at least 32 characters (256 bits) long.");
}

var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "TripDistribution.Api";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "TripDistribution.Client";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = builder.Environment.IsProduction();
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

    // Database-backed Real-Time Token Validation & Single-Session Revocation Check
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var authDataService = context.HttpContext.RequestServices.GetRequiredService<IAuthDataService>();

            // Extract raw Bearer token from header
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

            // Verify authenticated user ID matches token owner identity
            var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.Principal?.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(userId) || tokenRecord.UserId != userId)
            {
                context.Fail("Token does not match authenticated user identity.");
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

// 7. Universal Global & Policy-Based Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.Append("Retry-After", "60");
        context.HttpContext.Response.ContentType = "application/json";
        var response = ApiResponse.Fail("Too many requests. Please slow down and try again later.");
        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(response), token);
    };

    // Stricter policy for Auth routes (Login, Register, OTP)
    var authLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthLimit", builder.Environment.IsDevelopment() ? 2000 : 30);
    options.AddFixedWindowLimiter("auth-policy", opt =>
    {
        opt.PermitLimit = authLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 10;
    });

    // Stricter policy for Contact Verification / Scraping Prevention
    options.AddFixedWindowLimiter("contact-sync-policy", opt =>
    {
        opt.PermitLimit = builder.Environment.IsDevelopment() ? 2000 : 15;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 5;
    });

    // Global Rate Limiter partitioned by User Identity or Client IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value ?? "";
        // Do not rate-limit Swagger UI in development
        if (path.StartsWith("/swagger") || path == "/")
        {
            return RateLimitPartition.GetNoLimiter("swagger");
        }

        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst("userId")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            // Authenticated user partition (120 req/min in prod)
            var authPermit = builder.Environment.IsDevelopment() ? 5000 : 120;
            return RateLimitPartition.GetFixedWindowLimiter($"user_{userId}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 20
            });
        }

        // Unauthenticated client partition by IP (60 req/min in prod)
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
        var anonPermit = builder.Environment.IsDevelopment() ? 5000 : 60;
        return RateLimitPartition.GetFixedWindowLimiter($"ip_{clientIp}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = anonPermit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 10
        });
    });
});

// 8. CORS Configuration (Restricted in Production, Permissive in Development)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            var allowedOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>()
                ?? new[] { "https://tripdistribution.com", "https://app.tripdistribution.com" };
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// 9. Defensive Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "0");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    
    // Content-Security-Policy:
    // Allow Swagger UI assets (inline scripts/styles, data: images) and dev tooling (BrowserLink and Hot Reload WebSockets)
    var isDev = app.Environment.IsDevelopment();
    var csp = isDev
        ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self' http: https: ws: wss:; frame-ancestors 'none';"
        : "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none';";

    context.Response.Headers.Append("Content-Security-Policy", csp);
    if (context.Request.IsHttps)
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }
    await next();
});

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
