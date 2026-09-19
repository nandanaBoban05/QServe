using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class ReportingServiceTests
{
    private readonly ApplicationDbContext _db;
    private readonly ReportingService _service;

    public ReportingServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new ReportingService(_db);
    }

    #region Daily Sales Summary Tests

    [Fact]
    public async Task GetDailySalesSummary_WithSeededData_ReturnsCorrectAggregationsAndPaymentModeBreakdown()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var table = new RestaurantTable { TableNumber = "T1", QRCodeData = "qr1" };
        _db.RestaurantTables.Add(table);

        var order1 = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.Approved,
            TotalAmount = 250m,
            CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment>
            {
                new() { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received, Amount = 250m }
            }
        };

        var order2 = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.Served,
            TotalAmount = 150m,
            CreatedAt = new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment>
            {
                new() { PaymentMode = PaymentModes.Cash, PaymentStatus = PaymentStatuses.Received, Amount = 150m }
            }
        };

        _db.Orders.AddRange(order1, order2);
        await _db.SaveChangesAsync();

        var result = await _service.GetDailySalesSummaryAsync(start, end);

        Assert.Equal(start, result.RangeStart);
        Assert.Equal(end, result.RangeEnd);
        Assert.Equal(400m, result.Revenue);
        Assert.Equal(2, result.OrderCount);
        Assert.Equal(200m, result.AvgOrderValue);
        Assert.Equal(2, result.RevenueByPaymentMode.Count);
        Assert.Equal(250m, result.RevenueByPaymentMode[PaymentModes.Online]);
        Assert.Equal(150m, result.RevenueByPaymentMode[PaymentModes.Cash]);
    }

    [Fact]
    public async Task GetDailySalesSummary_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var beforeOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            TotalAmount = 100m,
            CreatedAt = new DateTime(2026, 1, 1, 23, 59, 59, DateTimeKind.Utc),
            Payments = new List<Payment> { new() { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received, Amount = 100m } }
        };

        var inRangeOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            TotalAmount = 200m,
            CreatedAt = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment> { new() { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received, Amount = 200m } }
        };

        var afterOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            TotalAmount = 300m,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment> { new() { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received, Amount = 300m } }
        };

        _db.Orders.AddRange(beforeOrder, inRangeOrder, afterOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetDailySalesSummaryAsync(start, end);

        Assert.Equal(200m, result.Revenue);
        Assert.Equal(1, result.OrderCount);
        Assert.Equal(200m, result.AvgOrderValue);
    }

    [Fact]
    public async Task GetDailySalesSummary_ExcludesCancelledOrders_EvenIfPaymentWasReceived()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var cancelledOrder = new Order
        {
            OrderStatus = OrderStatuses.Cancelled,
            TotalAmount = 500m,
            CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment>
            {
                new() { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received, Amount = 500m }
            }
        };

        _db.Orders.Add(cancelledOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetDailySalesSummaryAsync(start, end);

        Assert.Equal(0m, result.Revenue);
        Assert.Equal(0, result.OrderCount);
        Assert.Equal(0m, result.AvgOrderValue);
        Assert.Empty(result.RevenueByPaymentMode);
    }

    [Fact]
    public async Task GetDailySalesSummary_ExcludesOrdersWithoutReceivedPayment()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var pendingOrder = new Order
        {
            OrderStatus = OrderStatuses.PendingPayment,
            TotalAmount = 300m,
            CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment>
            {
                new() { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Pending, Amount = 300m }
            }
        };

        var awaitingOrder = new Order
        {
            OrderStatus = OrderStatuses.AwaitingVerification,
            TotalAmount = 200m,
            CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment>
            {
                new() { PaymentMode = PaymentModes.Cash, PaymentStatus = PaymentStatuses.Pending, Amount = 200m }
            }
        };

        _db.Orders.AddRange(pendingOrder, awaitingOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetDailySalesSummaryAsync(start, end);

        Assert.Equal(0m, result.Revenue);
        Assert.Equal(0, result.OrderCount);
        Assert.Equal(0m, result.AvgOrderValue);
    }

    [Fact]
    public async Task GetDailySalesSummary_EmptyData_ReturnsZeroSummaryWithoutThrowing()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetDailySalesSummaryAsync(start, end);

        Assert.NotNull(result);
        Assert.Equal(0m, result.Revenue);
        Assert.Equal(0, result.OrderCount);
        Assert.Equal(0m, result.AvgOrderValue);
        Assert.Empty(result.RevenueByPaymentMode);
    }

    [Fact]
    public async Task GetDailySalesSummary_InvertedDateRange_ReturnsZeroSummary()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var order = new Order
        {
            OrderStatus = OrderStatuses.Served,
            TotalAmount = 100m,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            Payments = new List<Payment> { new() { PaymentMode = PaymentModes.Cash, PaymentStatus = PaymentStatuses.Received, Amount = 100m } }
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _service.GetDailySalesSummaryAsync(start, end);

        Assert.Equal(0m, result.Revenue);
        Assert.Equal(0, result.OrderCount);
    }

    #endregion

    #region Popular Items Report Tests

    [Fact]
    public async Task GetPopularItemsReport_WithSeededData_RanksCorrectlyByQuantityAndRevenue()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var itemA = new MenuItem { Name = "Burger", Price = 50m, ItemType = ItemTypes.Cooked, IsAvailable = true };
        var itemB = new MenuItem { Name = "Soda", Price = 10m, ItemType = ItemTypes.Beverage, IsAvailable = true };
        var itemC = new MenuItem { Name = "Steak", Price = 200m, ItemType = ItemTypes.Cooked, IsAvailable = true };
        _db.MenuItems.AddRange(itemA, itemB, itemC);
        await _db.SaveChangesAsync();

        var order = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = itemA.ItemID, Item = itemA, Quantity = 5, UnitPrice = 50m },   // 5 sold, 250 revenue
                new() { ItemID = itemB.ItemID, Item = itemB, Quantity = 20, UnitPrice = 10m },  // 20 sold, 200 revenue
                new() { ItemID = itemC.ItemID, Item = itemC, Quantity = 2, UnitPrice = 200m }   // 2 sold, 400 revenue
            }
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var (byQuantity, byRevenue) = await _service.GetPopularItemsReportAsync(start, end, topN: 10);

        Assert.Equal(3, byQuantity.Count);
        Assert.Equal("Soda", byQuantity[0].ItemName);
        Assert.Equal(20, byQuantity[0].QuantitySold);
        Assert.Equal("Burger", byQuantity[1].ItemName);
        Assert.Equal(5, byQuantity[1].QuantitySold);
        Assert.Equal("Steak", byQuantity[2].ItemName);
        Assert.Equal(2, byQuantity[2].QuantitySold);

        Assert.Equal(3, byRevenue.Count);
        Assert.Equal("Steak", byRevenue[0].ItemName);
        Assert.Equal(400m, byRevenue[0].Revenue);
        Assert.Equal("Burger", byRevenue[1].ItemName);
        Assert.Equal(250m, byRevenue[1].Revenue);
        Assert.Equal("Soda", byRevenue[2].ItemName);
        Assert.Equal(200m, byRevenue[2].Revenue);
    }

    [Fact]
    public async Task GetPopularItemsReport_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var item = new MenuItem { Name = "Pizza", Price = 100m, ItemType = ItemTypes.Cooked, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var oldOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = 10, UnitPrice = 100m } }
        };

        var validOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = 3, UnitPrice = 100m } }
        };

        _db.Orders.AddRange(oldOrder, validOrder);
        await _db.SaveChangesAsync();

        var (byQuantity, byRevenue) = await _service.GetPopularItemsReportAsync(start, end);

        Assert.Single(byQuantity);
        Assert.Equal(3, byQuantity[0].QuantitySold);
        Assert.Equal(300m, byRevenue[0].Revenue);
    }

    [Fact]
    public async Task GetPopularItemsReport_ExcludesCancelledOrders()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var item = new MenuItem { Name = "Pasta", Price = 80m, ItemType = ItemTypes.Cooked, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var cancelledOrder = new Order
        {
            OrderStatus = OrderStatuses.Cancelled,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = 5, UnitPrice = 80m } }
        };

        _db.Orders.Add(cancelledOrder);
        await _db.SaveChangesAsync();

        var (byQuantity, byRevenue) = await _service.GetPopularItemsReportAsync(start, end);

        Assert.Empty(byQuantity);
        Assert.Empty(byRevenue);
    }

    [Fact]
    public async Task GetPopularItemsReport_RespectsTopNParameter()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 5; i++)
        {
            var item = new MenuItem { Name = $"Item{i}", Price = i * 10m, ItemType = ItemTypes.Quick, IsAvailable = true };
            _db.MenuItems.Add(item);
            await _db.SaveChangesAsync();

            var order = new Order
            {
                OrderStatus = OrderStatuses.Served,
                CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = i, UnitPrice = i * 10m } }
            };
            _db.Orders.Add(order);
        }
        await _db.SaveChangesAsync();

        var (byQuantity, byRevenue) = await _service.GetPopularItemsReportAsync(start, end, topN: 2);

        Assert.Equal(2, byQuantity.Count);
        Assert.Equal(2, byRevenue.Count);
    }

    [Fact]
    public async Task GetPopularItemsReport_EmptyData_ReturnsEmptyLists()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var (byQuantity, byRevenue) = await _service.GetPopularItemsReportAsync(start, end);

        Assert.Empty(byQuantity);
        Assert.Empty(byRevenue);
    }

    [Fact]
    public async Task GetPopularItemsReport_InvertedDateRange_ReturnsEmptyLists()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var (byQuantity, byRevenue) = await _service.GetPopularItemsReportAsync(start, end);

        Assert.Empty(byQuantity);
        Assert.Empty(byRevenue);
    }

    #endregion

    #region Kitchen Throughput Tests

    [Fact]
    public async Task GetKitchenThroughput_WithSeededData_ComputesAvgPrepTimeGroupedByItemType()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var itemCooked = new MenuItem { Name = "Burger", ItemType = ItemTypes.Cooked, Price = 50m, IsAvailable = true };
        var itemBeverage = new MenuItem { Name = "Juice", ItemType = ItemTypes.Beverage, Price = 20m, IsAvailable = true };
        _db.MenuItems.AddRange(itemCooked, itemBeverage);
        await _db.SaveChangesAsync();

        // Order 1: Cooked only, prep time 10 mins (12:00 -> 12:10)
        var order1 = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 11, 55, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ServedAt = new DateTime(2026, 1, 1, 12, 10, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = itemCooked.ItemID, Item = itemCooked, Quantity = 1, UnitPrice = 50m } }
        };

        // Order 2: Cooked only, prep time 20 mins (12:30 -> 12:50)
        var order2 = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 12, 25, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc),
            ServedAt = new DateTime(2026, 1, 1, 12, 50, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = itemCooked.ItemID, Item = itemCooked, Quantity = 2, UnitPrice = 50m } }
        };

        // Order 3: Beverage only, prep time 4 mins (13:00 -> 13:04)
        var order3 = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 12, 58, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc),
            ServedAt = new DateTime(2026, 1, 1, 13, 4, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = itemBeverage.ItemID, Item = itemBeverage, Quantity = 1, UnitPrice = 20m } }
        };

        _db.Orders.AddRange(order1, order2, order3);
        await _db.SaveChangesAsync();

        var result = await _service.GetKitchenThroughputAsync(start, end);

        Assert.Equal(2, result.Count);

        var beverageMetric = result.First(x => x.ItemType == ItemTypes.Beverage);
        Assert.Equal(4.0, beverageMetric.AvgPrepMinutes);
        Assert.Equal(1, beverageMetric.OrdersCompleted);

        var cookedMetric = result.First(x => x.ItemType == ItemTypes.Cooked);
        Assert.Equal(15.0, cookedMetric.AvgPrepMinutes); // (10 + 20) / 2 = 15
        Assert.Equal(2, cookedMetric.OrdersCompleted);
    }

    [Fact]
    public async Task GetKitchenThroughput_OrderWithMultipleDistinctItemTypes_CreditsDurationToEachType()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var itemCooked = new MenuItem { Name = "Curry", ItemType = ItemTypes.Cooked, Price = 100m, IsAvailable = true };
        var itemDessert = new MenuItem { Name = "Cake", ItemType = ItemTypes.Dessert, Price = 40m, IsAvailable = true };
        _db.MenuItems.AddRange(itemCooked, itemDessert);
        await _db.SaveChangesAsync();

        // Order contains both Cooked and Dessert. Duration = 12 mins
        var order = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc),
            ServedAt = new DateTime(2026, 1, 1, 12, 17, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem>
            {
                new() { ItemID = itemCooked.ItemID, Item = itemCooked, Quantity = 1, UnitPrice = 100m },
                new() { ItemID = itemDessert.ItemID, Item = itemDessert, Quantity = 2, UnitPrice = 40m }
            }
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _service.GetKitchenThroughputAsync(start, end);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.ItemType == ItemTypes.Cooked && x.AvgPrepMinutes == 12.0 && x.OrdersCompleted == 1);
        Assert.Contains(result, x => x.ItemType == ItemTypes.Dessert && x.AvgPrepMinutes == 12.0 && x.OrdersCompleted == 1);
    }

    [Fact]
    public async Task GetKitchenThroughput_ExcludesOrdersWithoutApprovedAtOrServedAt()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var item = new MenuItem { Name = "Pizza", ItemType = ItemTypes.Cooked, Price = 100m, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var unapprovedOrder = new Order
        {
            OrderStatus = OrderStatuses.PendingPayment,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ApprovedAt = null,
            ServedAt = null,
            OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = 1, UnitPrice = 100m } }
        };

        var unservedOrder = new Order
        {
            OrderStatus = OrderStatuses.Preparing,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc),
            ServedAt = null,
            OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = 1, UnitPrice = 100m } }
        };

        _db.Orders.AddRange(unapprovedOrder, unservedOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetKitchenThroughputAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetKitchenThroughput_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var item = new MenuItem { Name = "Soup", ItemType = ItemTypes.Cooked, Price = 30m, IsAvailable = true };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var outOfRangeOrder = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc),
            ServedAt = new DateTime(2026, 1, 1, 12, 20, 0, DateTimeKind.Utc),
            OrderItems = new List<OrderItem> { new() { ItemID = item.ItemID, Item = item, Quantity = 1, UnitPrice = 30m } }
        };

        _db.Orders.Add(outOfRangeOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetKitchenThroughputAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetKitchenThroughput_EmptyData_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetKitchenThroughputAsync(start, end);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetKitchenThroughput_InvertedDateRange_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetKitchenThroughputAsync(start, end);

        Assert.Empty(result);
    }

    #endregion

    #region Peak Hours Tests

    [Fact]
    public async Task GetPeakHours_WithSeededData_GroupsByHourAndSortsAscending()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var order1 = new Order { OrderStatus = OrderStatuses.Approved, CreatedAt = new DateTime(2026, 1, 1, 12, 10, 0, DateTimeKind.Utc) };
        var order2 = new Order { OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 12, 45, 0, DateTimeKind.Utc) };
        var order3 = new Order { OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 18, 30, 0, DateTimeKind.Utc) };

        _db.Orders.AddRange(order1, order2, order3);
        await _db.SaveChangesAsync();

        var result = await _service.GetPeakHoursAsync(start, end);

        Assert.Equal(2, result.Count);
        Assert.Equal(12, result[0].Hour);
        Assert.Equal(2, result[0].OrderCount);
        Assert.Equal(18, result[1].Hour);
        Assert.Equal(1, result[1].OrderCount);
    }

    [Fact]
    public async Task GetPeakHours_ExcludesCancelledOrders()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var cancelledOrder = new Order
        {
            OrderStatus = OrderStatuses.Cancelled,
            CreatedAt = new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc)
        };

        _db.Orders.Add(cancelledOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetPeakHoursAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPeakHours_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var outOfRange = new Order
        {
            OrderStatus = OrderStatuses.Served,
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        _db.Orders.Add(outOfRange);
        await _db.SaveChangesAsync();

        var result = await _service.GetPeakHoursAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPeakHours_EmptyData_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetPeakHoursAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPeakHours_InvertedDateRange_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetPeakHoursAsync(start, end);

        Assert.Empty(result);
    }

    #endregion

    #region Payment Verification Log Tests

    [Fact]
    public async Task GetPaymentVerificationLog_WithSeededData_ReturnsOfflinePaymentsOrderedDescending()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var admin = new User { UserName = "admin@qserve.test", FullName = "Admin User", Role = UserRoles.Admin };
        var table = new RestaurantTable { TableNumber = "Table-5", QRCodeData = "qr5" };
        _db.Users.Add(admin);
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var order1 = new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = 150m };
        var order2 = new Order { Table = table, OrderStatus = OrderStatuses.PendingPayment, TotalAmount = 80m };
        _db.Orders.AddRange(order1, order2);
        await _db.SaveChangesAsync();

        var pay1 = new Payment
        {
            OrderID = order1.OrderID,
            Order = order1,
            PaymentMode = PaymentModes.Cash,
            PaymentStatus = PaymentStatuses.Received,
            Amount = 150m,
            VerifiedBy = admin.Id,
            VerifiedByUser = admin,
            VerificationTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)
        };

        var pay2 = new Payment
        {
            OrderID = order2.OrderID,
            Order = order2,
            PaymentMode = PaymentModes.Card,
            PaymentStatus = PaymentStatuses.Failed,
            Amount = 80m,
            VerifiedBy = admin.Id,
            VerifiedByUser = admin,
            VerificationTime = new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc),
            RejectionReason = "Invalid PIN"
        };

        _db.Payments.AddRange(pay1, pay2);
        await _db.SaveChangesAsync();

        var result = await _service.GetPaymentVerificationLogAsync(start, end);

        Assert.Equal(2, result.Count);
        // Ordered descending by VerificationTime -> pay2 first, then pay1
        Assert.Equal(pay2.PaymentID, result[0].PaymentId);
        Assert.Equal("Table-5", result[0].TableNumber);
        Assert.Equal(PaymentModes.Card, result[0].PaymentMode);
        Assert.Equal(80m, result[0].Amount);
        Assert.False(result[0].Approved);
        Assert.Equal("Admin User", result[0].VerifiedByName);

        Assert.Equal(pay1.PaymentID, result[1].PaymentId);
        Assert.Equal(PaymentModes.Cash, result[1].PaymentMode);
        Assert.Equal(150m, result[1].Amount);
        Assert.True(result[1].Approved);
    }

    [Fact]
    public async Task GetPaymentVerificationLog_ExcludesOnlinePayments()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var onlinePayment = new Payment
        {
            PaymentMode = PaymentModes.Online,
            PaymentStatus = PaymentStatuses.Received,
            Amount = 200m,
            VerificationTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        _db.Payments.Add(onlinePayment);
        await _db.SaveChangesAsync();

        var result = await _service.GetPaymentVerificationLogAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPaymentVerificationLog_ExcludesUnverifiedPayments()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var unverifiedPayment = new Payment
        {
            PaymentMode = PaymentModes.Cash,
            PaymentStatus = PaymentStatuses.Pending,
            Amount = 100m,
            VerificationTime = null
        };
        _db.Payments.Add(unverifiedPayment);
        await _db.SaveChangesAsync();

        var result = await _service.GetPaymentVerificationLogAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPaymentVerificationLog_ExcludesPaymentsOutsideVerificationDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var payment = new Payment
        {
            PaymentMode = PaymentModes.Cash,
            PaymentStatus = PaymentStatuses.Received,
            Amount = 100m,
            VerificationTime = new DateTime(2026, 1, 1, 22, 0, 0, DateTimeKind.Utc)
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var result = await _service.GetPaymentVerificationLogAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPaymentVerificationLog_EmptyData_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetPaymentVerificationLogAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPaymentVerificationLog_InvertedDateRange_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetPaymentVerificationLogAsync(start, end);

        Assert.Empty(result);
    }

    #endregion

    #region Order Status Report Tests

    [Fact]
    public async Task GetOrderStatusReport_WithSeededData_CountsAllStatusesAndCalculatesCancellationRate()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var orders = new List<Order>
        {
            new() { OrderStatus = OrderStatuses.Approved, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) },
            new() { OrderStatus = OrderStatuses.Preparing, CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc) },
            new() { OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc) },
            new() { OrderStatus = OrderStatuses.Cancelled, CreatedAt = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc) }
        };

        _db.Orders.AddRange(orders);
        await _db.SaveChangesAsync();

        var result = await _service.GetOrderStatusReportAsync(start, end);

        Assert.Equal(4, result.TotalOrders);
        Assert.Equal(25.0, result.CancellationRatePercent); // 1 out of 4 = 25%
        Assert.Equal(4, result.CountsByStatus.Count);
        Assert.Equal(1, result.CountsByStatus[OrderStatuses.Approved]);
        Assert.Equal(1, result.CountsByStatus[OrderStatuses.Preparing]);
        Assert.Equal(1, result.CountsByStatus[OrderStatuses.Served]);
        Assert.Equal(1, result.CountsByStatus[OrderStatuses.Cancelled]);
    }

    [Fact]
    public async Task GetOrderStatusReport_AllCancelledOrders_Returns100PercentCancellationRate()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        _db.Orders.Add(new Order { OrderStatus = OrderStatuses.Cancelled, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) });
        _db.Orders.Add(new Order { OrderStatus = OrderStatuses.Cancelled, CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync();

        var result = await _service.GetOrderStatusReportAsync(start, end);

        Assert.Equal(2, result.TotalOrders);
        Assert.Equal(100.0, result.CancellationRatePercent);
    }

    [Fact]
    public async Task GetOrderStatusReport_ZeroCancelledOrders_Returns0PercentCancellationRate()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        _db.Orders.Add(new Order { OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync();

        var result = await _service.GetOrderStatusReportAsync(start, end);

        Assert.Equal(1, result.TotalOrders);
        Assert.Equal(0.0, result.CancellationRatePercent);
    }

    [Fact]
    public async Task GetOrderStatusReport_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var outOfRange = new Order { OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) };
        var inRange = new Order { OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc) };

        _db.Orders.AddRange(outOfRange, inRange);
        await _db.SaveChangesAsync();

        var result = await _service.GetOrderStatusReportAsync(start, end);

        Assert.Equal(1, result.TotalOrders);
    }

    [Fact]
    public async Task GetOrderStatusReport_EmptyData_ReturnsZeroTotalAndRateWithoutThrowing()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetOrderStatusReportAsync(start, end);

        Assert.Equal(0, result.TotalOrders);
        Assert.Equal(0.0, result.CancellationRatePercent);
        Assert.Empty(result.CountsByStatus);
    }

    [Fact]
    public async Task GetOrderStatusReport_InvertedDateRange_ReturnsZeroTotalAndRate()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetOrderStatusReportAsync(start, end);

        Assert.Equal(0, result.TotalOrders);
        Assert.Equal(0.0, result.CancellationRatePercent);
    }

    #endregion

    #region Table Utilisation Report Tests

    [Fact]
    public async Task GetTableUtilisationReport_WithSeededData_RanksTablesByRevenueDescending()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var table1 = new RestaurantTable { TableNumber = "T1", QRCodeData = "qr1" };
        var table2 = new RestaurantTable { TableNumber = "T2", QRCodeData = "qr2" };
        _db.RestaurantTables.AddRange(table1, table2);
        await _db.SaveChangesAsync();

        var order1 = new Order { Table = table1, OrderStatus = OrderStatuses.Served, TotalAmount = 100m, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) };
        var order2 = new Order { Table = table1, OrderStatus = OrderStatuses.Served, TotalAmount = 150m, CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var order3 = new Order { Table = table2, OrderStatus = OrderStatuses.Served, TotalAmount = 400m, CreatedAt = new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc) };

        _db.Orders.AddRange(order1, order2, order3);
        await _db.SaveChangesAsync();

        var result = await _service.GetTableUtilisationReportAsync(start, end);

        Assert.Equal(2, result.Count);
        // Ranked by revenue desc: Table 2 (400) > Table 1 (250)
        Assert.Equal("T2", result[0].TableNumber);
        Assert.Equal(1, result[0].OrderCount);
        Assert.Equal(400m, result[0].Revenue);

        Assert.Equal("T1", result[1].TableNumber);
        Assert.Equal(2, result[1].OrderCount);
        Assert.Equal(250m, result[1].Revenue);
    }

    [Fact]
    public async Task GetTableUtilisationReport_ExcludesCancelledOrders()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var table = new RestaurantTable { TableNumber = "T3", QRCodeData = "qr3" };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var cancelledOrder = new Order { Table = table, OrderStatus = OrderStatuses.Cancelled, TotalAmount = 500m, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) };
        _db.Orders.Add(cancelledOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetTableUtilisationReportAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTableUtilisationReport_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var table = new RestaurantTable { TableNumber = "T4", QRCodeData = "qr4" };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var outOfRange = new Order { Table = table, OrderStatus = OrderStatuses.Served, TotalAmount = 100m, CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) };
        _db.Orders.Add(outOfRange);
        await _db.SaveChangesAsync();

        var result = await _service.GetTableUtilisationReportAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTableUtilisationReport_EmptyData_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetTableUtilisationReportAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTableUtilisationReport_InvertedDateRange_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetTableUtilisationReportAsync(start, end);

        Assert.Empty(result);
    }

    #endregion

    #region Table Utilisation By Hour Tests

    [Fact]
    public async Task GetTableUtilisationByHour_WithSeededData_GroupsByTableAndHour()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var tableA = new RestaurantTable { TableNumber = "T10", QRCodeData = "qr10" };
        var tableB = new RestaurantTable { TableNumber = "T20", QRCodeData = "qr20" };
        _db.RestaurantTables.AddRange(tableA, tableB);
        await _db.SaveChangesAsync();

        var order1 = new Order { Table = tableA, OrderStatus = OrderStatuses.Approved, CreatedAt = new DateTime(2026, 1, 1, 13, 10, 0, DateTimeKind.Utc) };
        var order2 = new Order { Table = tableA, OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 13, 40, 0, DateTimeKind.Utc) };
        var order3 = new Order { Table = tableB, OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 13, 20, 0, DateTimeKind.Utc) };
        var order4 = new Order { Table = tableA, OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 19, 0, 0, DateTimeKind.Utc) };

        _db.Orders.AddRange(order1, order2, order3, order4);
        await _db.SaveChangesAsync();

        var result = await _service.GetTableUtilisationByHourAsync(start, end);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, x => x.TableNumber == "T10" && x.Hour == 13 && x.OrderCount == 2);
        Assert.Contains(result, x => x.TableNumber == "T20" && x.Hour == 13 && x.OrderCount == 1);
        Assert.Contains(result, x => x.TableNumber == "T10" && x.Hour == 19 && x.OrderCount == 1);
    }

    [Fact]
    public async Task GetTableUtilisationByHour_ExcludesCancelledOrders()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var table = new RestaurantTable { TableNumber = "T11", QRCodeData = "qr11" };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var cancelledOrder = new Order { Table = table, OrderStatus = OrderStatuses.Cancelled, CreatedAt = new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc) };
        _db.Orders.Add(cancelledOrder);
        await _db.SaveChangesAsync();

        var result = await _service.GetTableUtilisationByHourAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTableUtilisationByHour_ExcludesOrdersOutsideDateRange()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var table = new RestaurantTable { TableNumber = "T12", QRCodeData = "qr12" };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var outOfRange = new Order { Table = table, OrderStatus = OrderStatuses.Served, CreatedAt = new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc) };
        _db.Orders.Add(outOfRange);
        await _db.SaveChangesAsync();

        var result = await _service.GetTableUtilisationByHourAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTableUtilisationByHour_EmptyData_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetTableUtilisationByHourAsync(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTableUtilisationByHour_InvertedDateRange_ReturnsEmptyList()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetTableUtilisationByHourAsync(start, end);

        Assert.Empty(result);
    }

    #endregion
}
