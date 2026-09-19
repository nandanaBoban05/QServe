using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class RecentOrderedRecalculationServiceTests
{
    private readonly ApplicationDbContext _db;
    private readonly RecommendationService _recommendationService;

    public RecentOrderedRecalculationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _recommendationService = new RecommendationService(_db);
    }

    [Fact]
    public async Task RecomputeRecentOrdered_OrdersInsideAndOutsideWindow_CalculatesOnlyWithin7Days()
    {
        var itemA = new MenuItem { Name = "Burger", Price = 50m, TotalOrdered = 100, RecentOrdered = 0, IsAvailable = true };
        var itemB = new MenuItem { Name = "Pizza", Price = 100m, TotalOrdered = 50, RecentOrdered = 0, IsAvailable = true };
        _db.MenuItems.AddRange(itemA, itemB);
        await _db.SaveChangesAsync();

        // Order 1: 2 days ago (inside window), 3 Burger, 1 Pizza
        var recentOrder1 = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = itemA.ItemID, Item = itemA, Quantity = 3, UnitPrice = 50m },
                new() { ItemID = itemB.ItemID, Item = itemB, Quantity = 1, UnitPrice = 100m }
            }
        };

        // Order 2: 5 days ago (inside window), 2 Burger
        var recentOrder2 = new Order
        {
            OrderStatus = OrderStatuses.Approved,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = itemA.ItemID, Item = itemA, Quantity = 2, UnitPrice = 50m }
            }
        };

        // Order 3: 10 days ago (outside 7-day window), 10 Burger, 5 Pizza
        var oldOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = itemA.ItemID, Item = itemA, Quantity = 10, UnitPrice = 50m },
                new() { ItemID = itemB.ItemID, Item = itemB, Quantity = 5, UnitPrice = 100m }
            }
        };

        _db.Orders.AddRange(recentOrder1, recentOrder2, oldOrder);
        await _db.SaveChangesAsync();

        await _recommendationService.RecomputeRecentOrderedAsync();

        var reloadedItemA = await _db.MenuItems.FindAsync(itemA.ItemID);
        var reloadedItemB = await _db.MenuItems.FindAsync(itemB.ItemID);

        Assert.NotNull(reloadedItemA);
        Assert.NotNull(reloadedItemB);
        Assert.Equal(5, reloadedItemA.RecentOrdered); // 3 + 2 (10 from old order excluded)
        Assert.Equal(1, reloadedItemB.RecentOrdered); // 1 (5 from old order excluded)
    }

    [Fact]
    public async Task RecomputeRecentOrdered_ExcludesCancelledOrders()
    {
        var item = new MenuItem { Name = "Pasta", Price = 70m, RecentOrdered = 10, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var validOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item.ItemID, Item = item, Quantity = 4, UnitPrice = 70m }
            }
        };

        var cancelledOrder = new Order
        {
            OrderStatus = OrderStatuses.Cancelled,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item.ItemID, Item = item, Quantity = 6, UnitPrice = 70m }
            }
        };

        _db.Orders.AddRange(validOrder, cancelledOrder);
        await _db.SaveChangesAsync();

        await _recommendationService.RecomputeRecentOrderedAsync();

        var reloadedItem = await _db.MenuItems.FindAsync(item.ItemID);
        Assert.NotNull(reloadedItem);
        Assert.Equal(4, reloadedItem.RecentOrdered); // Cancelled 6 ignored
    }

    [Fact]
    public async Task RecomputeRecentOrdered_BoundaryCaseAtExactEdgeOfRollingWindow()
    {
        var item = new MenuItem { Name = "Soup", Price = 30m, RecentOrdered = 0, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        // 6 days 23 hours 50 mins ago (strictly inside 7-day window)
        var insideBoundaryOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-7).AddMinutes(10),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item.ItemID, Item = item, Quantity = 3, UnitPrice = 30m }
            }
        };

        // 7 days 10 mins ago (outside 7-day window)
        var outsideBoundaryOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-7).AddMinutes(-10),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item.ItemID, Item = item, Quantity = 5, UnitPrice = 30m }
            }
        };

        _db.Orders.AddRange(insideBoundaryOrder, outsideBoundaryOrder);
        await _db.SaveChangesAsync();

        await _recommendationService.RecomputeRecentOrderedAsync();

        var reloadedItem = await _db.MenuItems.FindAsync(item.ItemID);
        Assert.NotNull(reloadedItem);
        Assert.Equal(3, reloadedItem.RecentOrdered);
    }

    [Fact]
    public async Task RecomputeRecentOrdered_Idempotency_RunningTwiceProducesSameResult()
    {
        var item = new MenuItem { Name = "Salad", Price = 40m, RecentOrdered = 0, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var order = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item.ItemID, Item = item, Quantity = 7, UnitPrice = 40m }
            }
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // First run
        await _recommendationService.RecomputeRecentOrderedAsync();
        var firstRunItem = await _db.MenuItems.FindAsync(item.ItemID);
        Assert.NotNull(firstRunItem);
        Assert.Equal(7, firstRunItem.RecentOrdered);

        // Second run on unmodified data
        await _recommendationService.RecomputeRecentOrderedAsync();
        var secondRunItem = await _db.MenuItems.FindAsync(item.ItemID);
        Assert.NotNull(secondRunItem);
        Assert.Equal(7, secondRunItem.RecentOrdered);
    }

    [Fact]
    public async Task RecomputeRecentOrdered_EmptyDatabase_CompletesSuccessfullyWithZeroCounters()
    {
        var item = new MenuItem { Name = "Tea", Price = 15m, RecentOrdered = 25, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        // No orders exist in database
        await _recommendationService.RecomputeRecentOrderedAsync();

        var reloaded = await _db.MenuItems.FindAsync(item.ItemID);
        Assert.NotNull(reloaded);
        Assert.Equal(0, reloaded.RecentOrdered);
    }

    [Fact]
    public async Task RecomputeRecentOrdered_MultipleItemsAcrossMultipleOrders_AggregatesCorrectly()
    {
        var item1 = new MenuItem { Name = "Coffee", Price = 25m, RecentOrdered = 0, IsAvailable = true };
        var item2 = new MenuItem { Name = "Sandwich", Price = 60m, RecentOrdered = 0, IsAvailable = true };
        var item3 = new MenuItem { Name = "Juice", Price = 30m, RecentOrdered = 0, IsAvailable = true };
        _db.MenuItems.AddRange(item1, item2, item3);
        await _db.SaveChangesAsync();

        var orderA = new Order
        {
            OrderStatus = OrderStatuses.Approved,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item1.ItemID, Item = item1, Quantity = 2, UnitPrice = 25m },
                new() { ItemID = item2.ItemID, Item = item2, Quantity = 1, UnitPrice = 60m }
            }
        };

        var orderB = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = DateTime.UtcNow.AddDays(-4),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = item1.ItemID, Item = item1, Quantity = 3, UnitPrice = 25m },
                new() { ItemID = item3.ItemID, Item = item3, Quantity = 5, UnitPrice = 30m }
            }
        };

        _db.Orders.AddRange(orderA, orderB);
        await _db.SaveChangesAsync();

        await _recommendationService.RecomputeRecentOrderedAsync();

        var rItem1 = await _db.MenuItems.FindAsync(item1.ItemID);
        var rItem2 = await _db.MenuItems.FindAsync(item2.ItemID);
        var rItem3 = await _db.MenuItems.FindAsync(item3.ItemID);

        Assert.Equal(5, rItem1!.RecentOrdered); // 2 + 3
        Assert.Equal(1, rItem2!.RecentOrdered); // 1
        Assert.Equal(5, rItem3!.RecentOrdered); // 5
    }

    [Fact]
    public async Task RecentOrderedRecalculationService_ExecuteAsync_InvokesRecomputeInFreshScope()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var recommendationMock = new Mock<IRecommendationService>();
        var loggerMock = new Mock<ILogger<RecentOrderedRecalculationService>>();

        using var cts = new CancellationTokenSource();

        recommendationMock
            .Setup(r => r.RecomputeRecentOrderedAsync())
            .Returns(Task.CompletedTask)
            .Callback(() => cts.Cancel()); // Cancel as soon as first execution completes

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IRecommendationService)))
            .Returns(recommendationMock.Object);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var service = new RecentOrderedRecalculationService(scopeFactoryMock.Object, loggerMock.Object);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        recommendationMock.Verify(r => r.RecomputeRecentOrderedAsync(), Times.AtLeastOnce());
    }

    [Fact]
    public async Task RecentOrderedRecalculationService_ExecuteAsync_WhenRecomputeThrows_LogsErrorAndRecovers()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var recommendationMock = new Mock<IRecommendationService>();
        var loggerMock = new Mock<ILogger<RecentOrderedRecalculationService>>();

        using var cts = new CancellationTokenSource();

        recommendationMock
            .Setup(r => r.RecomputeRecentOrderedAsync())
            .ThrowsAsync(new InvalidOperationException("DB connection failure"))
            .Callback(() => cts.Cancel()); // Cancel after exception is caught

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IRecommendationService)))
            .Returns(recommendationMock.Object);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var service = new RecentOrderedRecalculationService(scopeFactoryMock.Object, loggerMock.Object);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        recommendationMock.Verify(r => r.RecomputeRecentOrderedAsync(), Times.AtLeastOnce());
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }
}
