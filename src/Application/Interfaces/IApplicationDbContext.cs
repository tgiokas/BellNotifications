using Microsoft.EntityFrameworkCore;
using BellNotificationEntity = BellNotification.Domain.Entities.BellNotification;
using NotificationStatusEntity = BellNotification.Domain.Entities.NotificationStatus;

namespace BellNotification.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<BellNotificationEntity> BellNotifications { get; }
    DbSet<NotificationStatusEntity> NotificationStatuses { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
