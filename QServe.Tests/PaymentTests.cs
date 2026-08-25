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
        order.Payment = new Payment { RazorpayOrderID = rzpOrderId, PaymentMode = PaymentModes.Online, PaymentStatus = paymentStatus };
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
        var validSigForOrderA = GenerateSignature("order_A_id", "pay_A_id", "fake_secret");

        var result = await _paymentService.ConfirmOnlinePaymentAsync(orderB.OrderID, "order_A_id", "pay_A_id", validSigForOrderA);

        Assert.Equal(ConfirmResult.OrderNotFound, result);
        Assert.NotEqual(OrderStatuses.Approved, orderB.OrderStatus);
    }

    [Fact]
    public async Task ConfirmOnlinePayment_WithInvalidSignature_ReturnsSignatureInvalid()
    {
        var order = await CreateOrderAsync("order_123");

        var result = await _paymentService.ConfirmOnlinePaymentAsync(order.OrderID, "order_123", "pay_456", "bad_sig");

        Assert.Equal(ConfirmResult.SignatureInvalid, result);
        Assert.NotEqual(OrderStatuses.Approved, order.OrderStatus);
    }

    [Fact]
    public async Task HandleWebhook_WithValidSignature_ApprovesPayment()
    {
        var order = await CreateOrderAsync("order_webhook");

        var payload = JsonSerializer.Serialize(new {
            @event = "payment.captured",
            payload = new {
                payment = new {
                    entity = new {
                        order_id = "order_webhook",
                        id = "pay_webhook"
                    }
                }
            }
        });

        var sig = GenerateWebhookSignature(payload, "fake_webhook_secret");

        var result = await _paymentService.HandleWebhookAsync(payload, sig);

        Assert.True(result);
        var dbOrder = await _db.Orders.Include(o => o.Payment).FirstAsync();
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
        var dbOrder = await _db.Orders.Include(o => o.Payment).FirstAsync();
        Assert.NotEqual(OrderStatuses.Approved, dbOrder.OrderStatus);
        Assert.NotEqual(PaymentStatuses.Received, dbOrder.Payment!.PaymentStatus);
    }

    [Fact]
    public async Task AdminVerify_Reject_CancelsOrderAndSetsPaymentFailed()
    {
        var order = await CreateOrderAsync("verify_reject", PaymentStatuses.Pending);
        order.OrderStatus = OrderStatuses.AwaitingVerification;
        await _db.SaveChangesAsync();

        await _paymentService.AdminVerifyAsync(order.Payment!.PaymentID, approve: false, adminUserId: 1);

        var dbOrder = await _db.Orders.Include(o => o.Payment).FirstAsync();
        Assert.Equal(OrderStatuses.Cancelled, dbOrder.OrderStatus);
        Assert.Equal(PaymentStatuses.Failed, dbOrder.Payment!.PaymentStatus);
    }
}
