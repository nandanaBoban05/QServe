using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using QServe.Data;
using QServe.Hubs;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class PaymentTests
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<IOrderClassifier> _orderClassifierMock = new();
    private readonly Mock<IRealtimeNotifier> _realtimeMock = new();
    private readonly Mock<IRecommendationService> _recommendationServiceMock = new();
    private readonly PaymentService _paymentService;

    public PaymentTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        var inMemorySettings = new Dictionary<string, string?> {
            {"Razorpay:KeyId", "rzp_test_fake"},
            {"Razorpay:KeySecret", "fake_secret"},
            {"Razorpay:WebhookSecret", "fake_webhook_secret"},
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        // By default setup anything to return 200 OK with a dummy Razorpay order
        handlerMock.Protected()
           .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
           .ReturnsAsync(new HttpResponseMessage()
           {
               StatusCode = System.Net.HttpStatusCode.OK,
               Content = new StringContent("{\"id\":\"order_fake\"}")
           });

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://api.razorpay.com/v1/") };
        _httpClientFactoryMock.Setup(x => x.CreateClient(nameof(PaymentService))).Returns(client);

        _paymentService = new PaymentService(
            _db, _config, _httpClientFactoryMock.Object, _orderClassifierMock.Object,
            _realtimeMock.Object, _recommendationServiceMock.Object);
    }

    private string GenerateSignature(string orderId, string paymentId, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{orderId}|{paymentId}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GenerateWebhookSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<Order> CreateOrderAsync(string rzpOrderId, string paymentStatus = PaymentStatuses.Pending)
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);
        
        var order = new Order { Table = table, OrderStatus = OrderStatuses.PendingPayment, TotalAmount = 100 };
        var payment = new Payment
        {
            RazorpayOrderID = rzpOrderId,
            PaymentMode = PaymentModes.Online,
            PaymentStatus = paymentStatus,
            Amount = 100,
            CreatedAt = DateTime.UtcNow
        };
        order.Payments.Add(payment);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task ConfirmOnlinePayment_WithValidSignature_ApprovesOrder()
    {
        var order = await CreateOrderAsync("order_123");
        var sig = GenerateSignature("order_123", "pay_456", "fake_secret");

        var result = await _paymentService.ConfirmOnlinePaymentAsync(order.OrderID, "order_123", "pay_456", sig);

        Assert.Equal(ConfirmResult.Approved, result);
        Assert.Equal(OrderStatuses.Approved, order.OrderStatus);
        Assert.Equal(PaymentStatuses.Received, order.Payment!.PaymentStatus);
    }

    [Fact]
    public async Task ConfirmOnlinePayment_WithCrossOrderMismatch_ReturnsNotFound()
    {
        var orderB = await CreateOrderAsync("order_B_id");

        var result = await _paymentService.ConfirmOnlinePaymentAsync(orderB.OrderID, "order_A_id", "pay_999", "anysig");

        Assert.Equal(ConfirmResult.OrderNotFound, result);
    }

    [Fact]
    public async Task ConfirmOnlinePayment_WithInvalidSignature_ReturnsSignatureInvalid()
    {
        var order = await CreateOrderAsync("order_789");

        var result = await _paymentService.ConfirmOnlinePaymentAsync(order.OrderID, "order_789", "pay_999", "tampered_sig");

        Assert.Equal(ConfirmResult.SignatureInvalid, result);
        Assert.Equal(OrderStatuses.PendingPayment, order.OrderStatus);
    }

    [Fact]
    public async Task HandleWebhook_WithValidSignature_ApprovesPayment()
    {
        var order = await CreateOrderAsync("order_webhook_1");

        var payload = JsonSerializer.Serialize(new {
            @event = "payment.captured",
            payload = new {
                payment = new {
                    entity = new {
                        order_id = "order_webhook_1",
                        id = "pay_captured_123",
                        amount = 10000 // 100.00 INR in paise
                    }
                }
            }
        });

        var sig = GenerateWebhookSignature(payload, "fake_webhook_secret");

        var result = await _paymentService.HandleWebhookAsync(payload, sig);

        Assert.True(result);
        var dbOrder = await _db.Orders.Include(o => o.Payments).FirstAsync();
        Assert.Equal(OrderStatuses.Approved, dbOrder.OrderStatus);
        Assert.Equal(PaymentStatuses.Received, dbOrder.Payment!.PaymentStatus);
    }
    
    [Fact]
    public async Task HandleWebhook_WithAmountMismatch_RejectsApproval()
    {
        var order = await CreateOrderAsync("order_mismatch"); // amount is 100 in CreateOrderAsync (10000 paise)

        var payload = JsonSerializer.Serialize(new {
            @event = "payment.captured",
            payload = new {
                payment = new {
                    entity = new {
                        order_id = "order_mismatch",
                        id = "pay_mismatch",
                        amount = 5000 // 50.00 INR instead of 100.00 INR!
                    }
                }
            }
        });

        var sig = GenerateWebhookSignature(payload, "fake_webhook_secret");

        var result = await _paymentService.HandleWebhookAsync(payload, sig);

        Assert.True(result); // returns true to acknowledge webhook receipt
        var dbOrder = await _db.Orders.Include(o => o.Payments).FirstAsync();
        Assert.NotEqual(OrderStatuses.Approved, dbOrder.OrderStatus);
        Assert.NotEqual(PaymentStatuses.Received, dbOrder.Payment!.PaymentStatus);
    }

    [Fact]
    public async Task AdminVerify_Reject_SetsPaymentFailedAndPreservesOrderForRepayment()
    {
        var order = await CreateOrderAsync("verify_reject", PaymentStatuses.Pending);
        order.OrderStatus = OrderStatuses.AwaitingVerification;
        await _db.SaveChangesAsync();

        await _paymentService.AdminVerifyAsync(order.Payment!.PaymentID, approve: false, adminUserId: 1, rejectionReason: "Cash not received");

        var dbOrder = await _db.Orders.Include(o => o.Payments).FirstAsync();
        Assert.Equal(OrderStatuses.PendingPayment, dbOrder.OrderStatus);
        Assert.Equal(PaymentStatuses.Failed, dbOrder.Payment!.PaymentStatus);
        Assert.Equal("Cash not received", dbOrder.Payment!.RejectionReason);
    }

    [Fact]
    public async Task Repayment_PreservesHistoryAndCreatesNewPaymentAttempt()
    {
        var category = new MenuCategory { Name = "Fast Food", IsActive = true };
        var item = new MenuItem { Name = "Burger", Price = 150, Category = category, IsAvailable = true };
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var table = new RestaurantTable { TableNumber = "T2", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.PendingPayment,
            TotalAmount = 150
        };
        order.OrderItems.Add(new OrderItem { Item = item, ItemID = item.ItemID, Quantity = 1, UnitPrice = 150 });
        order.Payments.Add(new Payment
        {
            PaymentMode = PaymentModes.Cash,
            PaymentStatus = PaymentStatuses.Failed,
            RejectionReason = "Cash not received",
            Amount = 150,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var newPayment = await _paymentService.InitiateRepaymentAsync(order.OrderID, PaymentModes.Card);

        var dbOrder = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderID == order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(2, dbOrder.Payments.Count);
        Assert.Equal(OrderStatuses.AwaitingVerification, dbOrder.OrderStatus);
        Assert.Equal(PaymentModes.Card, dbOrder.Payment!.PaymentMode);
        Assert.Equal(PaymentStatuses.Pending, dbOrder.Payment!.PaymentStatus);
    }

    [Fact]
    public async Task InitiateRepaymentAsync_WhenItemUnavailable_ThrowsInvalidOperationException()
    {
        var category = new MenuCategory { Name = "Mains", IsActive = true };
        var item = new MenuItem { Name = "Burger Special", Price = 180, Category = category, IsAvailable = false }; // Unavailable now!
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var table = new RestaurantTable { TableNumber = "T2", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.PendingPayment,
            TotalAmount = 180
        };
        order.OrderItems.Add(new OrderItem { Item = item, ItemID = item.ItemID, Quantity = 1, UnitPrice = 180 });
        order.Payments.Add(new Payment
        {
            PaymentMode = PaymentModes.Cash,
            PaymentStatus = PaymentStatuses.Failed,
            RejectionReason = "Cancelled",
            Amount = 180,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _paymentService.InitiateRepaymentAsync(order.OrderID, PaymentModes.Card));

        Assert.Contains("no longer available", ex.Message);
    }

    [Fact]
    public async Task InitiateOnlinePaymentAsync_WhenOrderPendingPayment_ReusesExistingRazorpayOrderId()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.PendingPayment,
            TotalAmount = 250
        };
        var payment = new Payment
        {
            PaymentMode = PaymentModes.Online,
            PaymentStatus = PaymentStatuses.Pending,
            RazorpayOrderID = "order_existing_12345",
            Amount = 250,
            CreatedAt = DateTime.UtcNow
        };
        order.Payments.Add(payment);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // Calling InitiateOnlinePaymentAsync on an order that already has RazorpayOrderID
        var info = await _paymentService.InitiateOnlinePaymentAsync(order.OrderID);

        Assert.NotNull(info);
        Assert.Equal("order_existing_12345", info.RazorpayOrderId);
        Assert.Equal(25000, info.AmountInPaise);
        Assert.Equal(order.OrderID, info.OrderId);
    }

    [Fact]
    public async Task InitiateOnlinePaymentAsync_WhenOrderCancelled_ThrowsInvalidOperationException()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.Cancelled, // Cancelled!
            TotalAmount = 250
        };
        order.Payments.Add(new Payment
        {
            PaymentMode = PaymentModes.Online,
            PaymentStatus = PaymentStatuses.Pending,
            Amount = 250
        });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _paymentService.InitiateOnlinePaymentAsync(order.OrderID));

        Assert.Contains("not eligible for payment", ex.Message);
    }

    [Fact]
    public async Task InitiateOnlinePaymentAsync_WhenOrderApproved_ThrowsInvalidOperationException()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.Approved, // Already Approved!
            TotalAmount = 250
        };
        order.Payments.Add(new Payment
        {
            PaymentMode = PaymentModes.Online,
            PaymentStatus = PaymentStatuses.Received,
            Amount = 250
        });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _paymentService.InitiateOnlinePaymentAsync(order.OrderID));

        Assert.Contains("not eligible for payment", ex.Message);
    }

    [Fact]
    public async Task InitiateRepaymentAsync_WhenOrderCancelled_ThrowsInvalidOperationExceptionAndDoesNotCreatePayment()
    {
        var category = new MenuCategory { Name = "Mains", IsActive = true };
        var item = new MenuItem { Name = "Burger", Price = 150, Category = category, IsAvailable = true };
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.Cancelled, // Cancelled!
            TotalAmount = 150
        };
        order.OrderItems.Add(new OrderItem { Item = item, ItemID = item.ItemID, Quantity = 1, UnitPrice = 150 });
        order.Payments.Add(new Payment
        {
            PaymentMode = PaymentModes.Online,
            PaymentStatus = PaymentStatuses.Failed,
            RejectionReason = "Payment failed at gateway",
            Amount = 150
        });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var initialPaymentCount = order.Payments.Count;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _paymentService.InitiateRepaymentAsync(order.OrderID, PaymentModes.Cash));

        Assert.Contains("cancelled", ex.Message);

        // Verify no new payment attempt was added
        var dbOrder = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderID == order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(initialPaymentCount, dbOrder.Payments.Count);
        Assert.Equal(OrderStatuses.Cancelled, dbOrder.OrderStatus);
    }

    [Fact]
    public async Task InitiateRepaymentAsync_WhenOrderPendingPayment_CreatesNewPaymentAttemptAndPreservesHistory()
    {
        var category = new MenuCategory { Name = "Mains", IsActive = true };
        var item = new MenuItem { Name = "Burger", Price = 150, Category = category, IsAvailable = true };
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.PendingPayment,
            TotalAmount = 150
        };
        order.OrderItems.Add(new OrderItem { Item = item, ItemID = item.ItemID, Quantity = 1, UnitPrice = 150 });
        order.Payments.Add(new Payment
        {
            PaymentMode = PaymentModes.Online,
            PaymentStatus = PaymentStatuses.Failed,
            RejectionReason = "Gateway timeout",
            Amount = 150,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var newPayment = await _paymentService.InitiateRepaymentAsync(order.OrderID, PaymentModes.Cash);

        Assert.NotNull(newPayment);
        Assert.Equal(PaymentModes.Cash, newPayment.PaymentMode);
        Assert.Equal(PaymentStatuses.Pending, newPayment.PaymentStatus);

        var dbOrder = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderID == order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(2, dbOrder.Payments.Count);
        Assert.Equal(OrderStatuses.AwaitingVerification, dbOrder.OrderStatus); // Cash moves to AwaitingVerification
    }
}
