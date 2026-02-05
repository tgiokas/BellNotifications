using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;
using BellNotification.Application.Services;
using BellNotification.Infrastructure.Database;
using BellNotification.Infrastructure.Services;
using Xunit;
using Moq;
using BellNotification.Domain.Entities;

namespace BellNotification.Tests.Services;

public class NotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _service;
    private readonly ISseConnectionManager _sseManager;

    public NotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options)
        {
            BellNotifications = new Mock<DbSet<BellNotification.Domain.Entities.BellNotification>>().Object,
            NotificationStatuses = new Mock<DbSet<NotificationStatus>>().Object
        };

        _sseManager = new SseConnectionManager(Mock.Of<ILogger<SseConnectionManager>>());
        var logger = Mock.Of<ILogger<NotificationService>>();
        _service = new NotificationService(_context, logger, _sseManager);
    }

    [Fact]
    public async Task CreateNotification_WithDedupeKey_ShouldPreventDuplicates()
    {
        // Arrange
        var request1 = new CreateNotificationRequest
        {
            TenantId = "tenant1",
            UserId = "user1",
            Type = "test.type",
            Title = "Test",
            DedupeKey = "dedupe-123"
        };

        var request2 = new CreateNotificationRequest
        {
            TenantId = "tenant1",
            UserId = "user1",
            Type = "test.type",
            Title = "Test Duplicate",
            DedupeKey = "dedupe-123"
        };

        // Act
        var id1 = await _service.CreateNotificationAsync(request1);
        var id2 = await _service.CreateNotificationAsync(request2);

        // Assert
        Assert.Equal(id1, id2);
        var count = await _context.BellNotifications.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateNotification_WithoutDedupeKey_ShouldAllowDuplicates()
    {
        // Arrange
        var request1 = new CreateNotificationRequest
        {
            TenantId = "tenant1",
            UserId = "user1",
            Type = "test.type",
            Title = "Test"
        };

        var request2 = new CreateNotificationRequest
        {
            TenantId = "tenant1",
            UserId = "user1",
            Type = "test.type",
            Title = "Test"
        };

        // Act
        var id1 = await _service.CreateNotificationAsync(request1);
        var id2 = await _service.CreateNotificationAsync(request2);

        // Assert
        Assert.NotEqual(id1, id2);
        var count = await _context.BellNotifications.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetNotifications_WithCursor_ShouldPaginateCorrectly()
    {
        // Arrange
        var tenantId = "tenant1";
        var userId = "user1";

        // Create 5 notifications
        for (int i = 0; i < 5; i++)
        {
            await _service.CreateNotificationAsync(new CreateNotificationRequest
            {
                TenantId = tenantId,
                UserId = userId,
                Type = "test.type",
                Title = $"Test {i}"
            });
            await Task.Delay(10); // Ensure different timestamps
        }

        // Act - Get first page
        var page1 = await _service.GetNotificationsAsync(tenantId, userId, null, 2);
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);

        // Act - Get second page using cursor
        var page2 = await _service.GetNotificationsAsync(tenantId, userId, page1.NextCursor, 2);
        Assert.Equal(2, page2.Items.Count);
        Assert.NotNull(page2.NextCursor);

        // Act - Get third page
        var page3 = await _service.GetNotificationsAsync(tenantId, userId, page2.NextCursor, 2);
        Assert.Equal(1, page3.Items.Count);
        Assert.Null(page3.NextCursor);

        // Assert - All items are unique
        var allIds = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3.Items.Select(i => i.Id))
            .ToList();
        Assert.Equal(5, allIds.Distinct().Count());
    }

    [Fact]
    public async Task GetUnreadCount_ShouldExcludeReadAndDismissed()
    {
        // Arrange
        var tenantId = "tenant1";
        var userId = "user1";

        var id1 = await _service.CreateNotificationAsync(new CreateNotificationRequest
        {
            TenantId = tenantId,
            UserId = userId,
            Type = "test.type",
            Title = "Unread 1"
        });

        var id2 = await _service.CreateNotificationAsync(new CreateNotificationRequest
        {
            TenantId = tenantId,
            UserId = userId,
            Type = "test.type",
            Title = "Unread 2"
        });

        var id3 = await _service.CreateNotificationAsync(new CreateNotificationRequest
        {
            TenantId = tenantId,
            UserId = userId,
            Type = "test.type",
            Title = "Unread 3"
        });

        // Act - Check initial count
        var count1 = await _service.GetUnreadCountAsync(tenantId, userId);
        Assert.Equal(3, count1.UnreadCount);

        // Act - Mark one as read
        await _service.MarkAsReadAsync(tenantId, userId, id1);
        var count2 = await _service.GetUnreadCountAsync(tenantId, userId);
        Assert.Equal(2, count2.UnreadCount);

        // Act - Dismiss one
        await _service.DismissAsync(tenantId, userId, id2);
        var count3 = await _service.GetUnreadCountAsync(tenantId, userId);
        Assert.Equal(1, count3.UnreadCount);

        // Act - Mark all as read
        await _service.MarkAllAsReadAsync(tenantId, userId);
        var count4 = await _service.GetUnreadCountAsync(tenantId, userId);
        Assert.Equal(0, count4.UnreadCount);
    }

    [Fact]
    public async Task GetNotifications_ShouldRespectTenantIsolation()
    {
        // Arrange
        await _service.CreateNotificationAsync(new CreateNotificationRequest
        {
            TenantId = "tenant1",
            UserId = "user1",
            Type = "test.type",
            Title = "Tenant1 Notification"
        });

        await _service.CreateNotificationAsync(new CreateNotificationRequest
        {
            TenantId = "tenant2",
            UserId = "user1",
            Type = "test.type",
            Title = "Tenant2 Notification"
        });

        // Act
        var tenant1Notifications = await _service.GetNotificationsAsync("tenant1", "user1", null, 10);
        var tenant2Notifications = await _service.GetNotificationsAsync("tenant2", "user1", null, 10);

        // Assert
        Assert.Single(tenant1Notifications.Items);
        Assert.Equal("Tenant1 Notification", tenant1Notifications.Items[0].Title);
        Assert.Single(tenant2Notifications.Items);
        Assert.Equal("Tenant2 Notification", tenant2Notifications.Items[0].Title);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
