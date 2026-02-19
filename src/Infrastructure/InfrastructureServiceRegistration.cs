using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using BellNotification.Application.Interfaces;
using BellNotification.Domain.Interfaces;
using BellNotification.Infrastructure.Database;
using BellNotification.Infrastructure.Repositories;

namespace BellNotification.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration, string databaseProvider)
    {
        var connectionString = configuration["BELL_NOTIFICATION_DB_CONNECTION"]
            ?? throw new ArgumentNullException(nameof(configuration), "BELL_NOTIFICATION_DB_CONNECTION is null.");

        NpgsqlDataSource? dataSource = null;
        if (databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();
            dataSource = dataSourceBuilder.Build();
        }

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            if (databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                throw new ArgumentException($"Unsupported database provider: {databaseProvider}. Only 'postgresql' is supported.");
            }
        });
        
        services.AddScoped<IBellNotificationRepository, BellNotificationRepository>();
        services.AddScoped<INotificationStatusRepository, NotificationStatusRepository>();

        return services;
    }
}
