using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using BellNotification.Application.Interfaces;
using BellNotification.Application.Services;
using BellNotification.Infrastructure;
using BellNotification.Infrastructure.Database;
using BellNotification.Infrastructure.Messaging;
using BellNotification.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names to match frontend expectations
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });

// Register Database Context
builder.Services.AddDatabaseServices(builder.Configuration, "postgresql");

// Application services
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<ISseConnectionManager, SseConnectionManager>();

// Web Push - In-memory subscriptions store (POC)
builder.Services.AddSingleton<ConcurrentDictionary<string, PushSubscriptionData>>();

// Web Push Service
builder.Services.AddSingleton<IWebPushService, WebPushService>();

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
dbContext.Database.Migrate();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Database migrations applied (if any).");

// Important: disable response buffering for SSE
app.Use(async (context, next) =>
{
    context.Response.Body = new System.IO.BufferedStream(context.Response.Body, 1);
    await next();
});

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
