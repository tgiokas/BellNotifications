using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using BellNotification.Application.Interfaces;
using BellNotification.Application.Services;
using BellNotification.Infrastructure.Database;
using BellNotification.Infrastructure.Messaging;
using BellNotification.Infrastructure.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names to match frontend expectations
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });

// Database - Build connection string from environment variables
var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
var postgresPort = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5433";
var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bell_notifications_db";
var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
var postgresPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";

var connectionString = $"Host={postgresHost};Port={postgresPort};Database={postgresDb};Username={postgresUser};Password={postgresPassword}";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

// Application services
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<ISseConnectionManager, SseConnectionManager>();

// Kafka consumer for bell notifications
builder.Services.AddHostedService<KafkaBellNotificationConsumer>();

// Authentication - JWT Bearer token only (as other microservices)
var jwtAuthority = Environment.GetEnvironmentVariable("KEYCLOAK_AUTHORITY");
var jwtAudience = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENTID") ?? Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_ID");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrEmpty(jwtAuthority) && !string.IsNullOrEmpty(jwtAudience))
        {
            options.Authority = jwtAuthority;
            options.Audience = jwtAudience;
            options.RequireHttpsMetadata = bool.Parse(Environment.GetEnvironmentVariable("KEYCLOAK_REQUIRE_HTTPS_METADATA") ?? "false");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtAuthority,
                ValidateAudience = true,
                ValidAudiences = [jwtAudience],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
        }
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
        JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
// Map health checks early
app.MapHealthChecks("/health");

// Apply database migrations on startup (before middleware pipeline)
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Applying database migrations...");
dbContext.Database.Migrate();
logger.LogInformation("Database migrations applied (if any).");

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
