using Microsoft.EntityFrameworkCore;

using BellNotification.Application.Interfaces;
using BellNotification.Domain.Entities;

namespace BellNotification.Infrastructure.Database;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public required DbSet<Domain.Entities.BellNotification> BellNotifications { get; set; }
    public required DbSet<NotificationStatus> NotificationStatuses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure BellNotification
        modelBuilder.Entity<Domain.Entities.BellNotification>(entity =>
        {            
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Type).IsRequired().HasMaxLength(100);
            entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Body).HasMaxLength(2000);
            entity.Property(x => x.Link).HasMaxLength(500);
            entity.Property(x => x.Severity).HasMaxLength(50);
            entity.Property(x => x.SourceService).HasMaxLength(100);
            entity.Property(x => x.DedupeKey).HasMaxLength(200);
            entity.Property(x => x.TenantId).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            // Indexes for performance
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAtUtc })
                .HasDatabaseName("IX_BellNotification_TenantId_UserId_CreatedAtUtc");

            // Unique constraint on DedupeKey when provided (composite with TenantId and UserId)
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.DedupeKey })
                .HasDatabaseName("IX_BellNotification_DedupeKey")
                .IsUnique()
                .HasFilter("\"DedupeKey\" IS NOT NULL");
        });

        // Configure NotificationStatus
        modelBuilder.Entity<NotificationStatus>(entity =>
        {            
            entity.HasKey(x => new { x.NotificationId, x.UserId });
            
            entity.Property(x => x.UserId).IsRequired().HasMaxLength(200);
            entity.Property(x => x.TenantId).HasMaxLength(100);

            // Foreign key relationship
            entity.HasOne(x => x.Notification)
                .WithMany()
                .HasForeignKey(x => x.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for query performance
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ReadAtUtc, x.NotificationId })
                .HasDatabaseName("IX_NotificationStatus_TenantId_UserId_ReadAtUtc");

            entity.HasIndex(x => new { x.TenantId, x.UserId, x.NotificationId })
                .HasDatabaseName("IX_NotificationStatus_TenantId_UserId");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await base.SaveChangesAsync(cancellationToken);
    }
}
