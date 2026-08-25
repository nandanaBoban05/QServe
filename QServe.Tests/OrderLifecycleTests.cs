using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using QServe.Controllers;
using QServe.Data;
using QServe.Hubs;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class OrderLifecycleTests
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IQrCodeService> _qrMock = new();
    private readonly Mock<IRealtimeNotifier> _realtimeMock = new();
    private readonly Mock<IRecommendationService> _recMock = new();
    private readonly Mock<IPriorityQueueBuilder> _priorityQueueMock = new();
    private readonly CustomerController _customerController;
    private readonly KitchenController _kitchenController;

    public OrderLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _qrMock.Setup(q => q.ValidateToken(It.IsAny<int>(), It.IsAny<string>())).Returns(true);
        _priorityQueueMock.Setup(p => p.Build(It.IsAny<List<Order>>())).Returns<List<Order>>(orders => orders);

        _customerController = new CustomerController(_db, _qrMock.Object, _realtimeMock.Object, _recMock.Object);
        _kitchenController = new KitchenController(_db, _priorityQueueMock.Object, _realtimeMock.Object);

        SetupControllerContext(_customerController, "1", "Customer");
        SetupControllerContext(_kitchenController, "10", UserRoles.Kitchen);
    }

    private void SetupControllerContext(Controller controller, string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Session = new TestSession();

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public void OrderStatusStateMachine_ValidatesAllowedTransitions()
    {
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.PendingPayment, OrderStatuses.Approved));
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.PendingPayment, OrderStatuses.Cancelled));
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.AwaitingVerification, OrderStatuses.Approved));
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.Approved, OrderStatuses.Preparing));
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.Preparing, OrderStatuses.Ready));
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.Ready, OrderStatuses.Served));
        Assert.True(OrderStatusStateMachine.CanTransition(OrderStatuses.Served, OrderStatuses.Cancelled));
    }

    [Fact]
    public void OrderStatusStateMachine_RejectsInvalidTransitions()
    {
        Assert.False(OrderStatusStateMachine.CanTransition(OrderStatuses.Served, OrderStatuses.Preparing));
        Assert.False(OrderStatusStateMachine.CanTransition(OrderStatuses.Preparing, OrderStatuses.PendingPayment));
        Assert.False(OrderStatusStateMachine.CanTransition(OrderStatuses.Cancelled, OrderStatuses.Approved));
        Assert.False(OrderStatusStateMachine.CanTransition(OrderStatuses.Cancelled, OrderStatuses.Preparing));
        Assert.False(OrderStatusStateMachine.CanTransition(OrderStatuses.Approved, OrderStatuses.Served));
    }

    [Fact]
    public async Task Checkout_RecalculatesPricesFromDatabase_IgnoringTamperedCartPrice()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var category = new MenuCategory { Name = "Food", IsActive = true };
        var item = new MenuItem { Name = "Biryani", Price = 250, Category = category, IsAvailable = true };

        _db.RestaurantTables.Add(table);
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        // Setup session token and cart with tampered price (e.g. client sent 1 rupee)
        _customerController.HttpContext.Session.SetString($"token:table:{table.TableID}", "valid_token");
        var tamperedCart = new List<CartItem>
        {
            new CartItem { ItemID = item.ItemID, Name = item.Name, Quantity = 2, UnitPrice = 1.00m }
        };
        _customerController.HttpContext.Session.SetString($"cart:table:{table.TableID}", JsonSerializer.Serialize(tamperedCart));

        var result = await _customerController.Checkout(table.TableID, PaymentModes.Cash);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        var order = await _db.Orders.Include(o => o.OrderItems).Include(o => o.Payment).FirstAsync();

        // Must be recalculated using DB price 250 * 2 = 500
        Assert.Equal(500, order.TotalAmount);
        Assert.Equal(250, order.OrderItems.First().UnitPrice);
        Assert.Equal(500, order.Payment!.Amount);
    }

    [Fact]
    public async Task Checkout_WithUnavailableItem_AbortsAndRedirectsToCart()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var category = new MenuCategory { Name = "Food", IsActive = true };
        var item = new MenuItem { Name = "Special Curry", Price = 200, Category = category, IsAvailable = false }; // Unavailable!

        _db.RestaurantTables.Add(table);
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        _customerController.HttpContext.Session.SetString($"token:table:{table.TableID}", "valid_token");
        var cart = new List<CartItem>
        {
            new CartItem { ItemID = item.ItemID, Name = item.Name, Quantity = 1, UnitPrice = 200 }
        };
        _customerController.HttpContext.Session.SetString($"cart:table:{table.TableID}", JsonSerializer.Serialize(cart));

        var result = await _customerController.Checkout(table.TableID, PaymentModes.Cash);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Cart", redirect.ActionName);
        Assert.NotNull(_customerController.TempData["CartError"]);
        Assert.Empty(_db.Orders); // No order was created
    }

    [Fact]
    public async Task Kitchen_MarkPreparing_AdvancesStatusAndBroadcastsSignalR()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var order = new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = 200 };
        _db.RestaurantTables.Add(table);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _kitchenController.MarkPreparing(order.OrderID) as RedirectToActionResult;

        Assert.NotNull(result);
        var dbOrder = await _db.Orders.FindAsync(order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatuses.Preparing, dbOrder.OrderStatus);

        _realtimeMock.Verify(r => r.BroadcastOrderStatusUpdateAsync(
            It.Is<OrderStatusUpdateDto>(d => d.OrderID == order.OrderID && d.NewStatus == OrderStatuses.Preparing)),
            Times.Once);
    }

    [Fact]
    public async Task Kitchen_MarkReady_AdvancesStatusToReady()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var order = new Order { Table = table, OrderStatus = OrderStatuses.Preparing, TotalAmount = 200 };
        _db.RestaurantTables.Add(table);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _kitchenController.MarkReady(order.OrderID) as RedirectToActionResult;

        Assert.NotNull(result);
        var dbOrder = await _db.Orders.FindAsync(order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatuses.Ready, dbOrder.OrderStatus);
    }

    [Fact]
    public async Task Kitchen_MarkServed_SetsServedAtAndCompletesOrder()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var order = new Order { Table = table, OrderStatus = OrderStatuses.Ready, TotalAmount = 200 };
        _db.RestaurantTables.Add(table);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _kitchenController.MarkServed(order.OrderID) as RedirectToActionResult;

        Assert.NotNull(result);
        var dbOrder = await _db.Orders.FindAsync(order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatuses.Served, dbOrder.OrderStatus);
        Assert.NotNull(dbOrder.ServedAt);
    }

    [Fact]
    public async Task Kitchen_InvalidTransition_IsRejected()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        // Order is still in PendingPayment — cannot jump directly to Preparing!
        var order = new Order { Table = table, OrderStatus = OrderStatuses.PendingPayment, TotalAmount = 200 };
        _db.RestaurantTables.Add(table);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _kitchenController.MarkPreparing(order.OrderID) as RedirectToActionResult;

        Assert.NotNull(result);
        var dbOrder = await _db.Orders.FindAsync(order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatuses.PendingPayment, dbOrder.OrderStatus); // Status was NOT changed
        Assert.NotNull(_kitchenController.TempData["KdsError"]);
    }

    [Fact]
    public async Task Customer_Status_CannotAccessAnotherTableOrder_ReturnsInvalidTable()
    {
        var table1 = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var table2 = new RestaurantTable { TableNumber = "T2", IsActive = true };
        var order2 = new Order { Table = table2, TableID = table2.TableID, OrderStatus = OrderStatuses.Approved, TotalAmount = 150 };

        _db.RestaurantTables.AddRange(table1, table2);
        _db.Orders.Add(order2);
        await _db.SaveChangesAsync();

        // Customer session only has token and orders for Table 1
        _customerController.HttpContext.Session.SetString($"token:table:{table1.TableID}", "valid_token");
        _customerController.HttpContext.Session.SetString($"orders:table:{table1.TableID}", JsonSerializer.Serialize(new List<int> { 999 }));

        // Attempt to access Table 2's order
        var result = await _customerController.Status(order2.OrderID);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("InvalidTable", viewResult.ViewName);
    }

    [Fact]
    public async Task Customer_StatusJson_CannotAccessAnotherTableOrder_ReturnsForbid()
    {
        var table1 = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var table2 = new RestaurantTable { TableNumber = "T2", IsActive = true };
        var order2 = new Order { Table = table2, TableID = table2.TableID, OrderStatus = OrderStatuses.Approved, TotalAmount = 150 };

        _db.RestaurantTables.AddRange(table1, table2);
        _db.Orders.Add(order2);
        await _db.SaveChangesAsync();

        _customerController.HttpContext.Session.SetString($"token:table:{table1.TableID}", "valid_token");
        _customerController.HttpContext.Session.SetString($"orders:table:{table1.TableID}", JsonSerializer.Serialize(new List<int> { 999 }));

        var result = await _customerController.StatusJson(order2.OrderID);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Customer_Menu_WithInactiveTable_ReturnsInvalidTable()
    {
        var inactiveTable = new RestaurantTable { TableNumber = "T99", IsActive = false };
        _db.RestaurantTables.Add(inactiveTable);
        await _db.SaveChangesAsync();

        _customerController.HttpContext.Session.SetString($"token:table:{inactiveTable.TableID}", "valid_token");

        var result = await _customerController.Menu(inactiveTable.TableID, "valid_token");

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("InvalidTable", viewResult.ViewName);
    }

    [Fact]
    public async Task AddToCart_ClampsMaxQuantityToFifty()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var category = new MenuCategory { Name = "Drinks", IsActive = true };
        var item = new MenuItem { Name = "Lemonade", Price = 50, Category = category, IsAvailable = true };

        _db.RestaurantTables.Add(table);
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        _customerController.HttpContext.Session.SetString($"token:table:{table.TableID}", "valid_token");

        // Attempt to add 999 items
        await _customerController.AddToCart(table.TableID, item.ItemID, 999, null);

        var cartJson = _customerController.HttpContext.Session.GetString($"cart:table:{table.TableID}");
        Assert.NotNull(cartJson);
        var cart = JsonSerializer.Deserialize<List<CartItem>>(cartJson);
        Assert.NotNull(cart);
        Assert.Single(cart);
        Assert.Equal(50, cart.First().Quantity); // Clamped to 50
    }

    [Fact]
    public async Task UpdateCart_WithZeroQuantity_RemovesItemFromCart()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var category = new MenuCategory { Name = "Drinks", IsActive = true };
        var item = new MenuItem { Name = "Lemonade", Price = 50, Category = category, IsAvailable = true };

        _db.RestaurantTables.Add(table);
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        _customerController.HttpContext.Session.SetString($"token:table:{table.TableID}", "valid_token");
        var cart = new List<CartItem>
        {
            new CartItem { ItemID = item.ItemID, Name = item.Name, Quantity = 2, UnitPrice = 50 }
        };
        _customerController.HttpContext.Session.SetString($"cart:table:{table.TableID}", JsonSerializer.Serialize(cart));

        // Update quantity to 0
        _customerController.UpdateCart(table.TableID, item.ItemID, null, 0);

        var cartJson = _customerController.HttpContext.Session.GetString($"cart:table:{table.TableID}");
        Assert.NotNull(cartJson);
        var updatedCart = JsonSerializer.Deserialize<List<CartItem>>(cartJson);
        Assert.NotNull(updatedCart);
        Assert.Empty(updatedCart);
    }

    [Fact]
    public async Task Checkout_WithEmptyCart_RedirectsToCart()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        _customerController.HttpContext.Session.SetString($"token:table:{table.TableID}", "valid_token");
        // Empty cart
        _customerController.HttpContext.Session.SetString($"cart:table:{table.TableID}", JsonSerializer.Serialize(new List<CartItem>()));

        var result = await _customerController.Checkout(table.TableID, PaymentModes.Cash);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Cart", redirect.ActionName);
    }
}

// In-Memory implementation of ISession for controller testing
internal class TestSession : ISession
{
    private readonly Dictionary<string, byte[]> _store = new();

    public bool IsAvailable => true;
    public string Id => "test_session_id";
    public IEnumerable<string> Keys => _store.Keys;

    public void Clear() => _store.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public void Set(string key, byte[] value) => _store[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
}
