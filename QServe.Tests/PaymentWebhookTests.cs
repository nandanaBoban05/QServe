using System.IO;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using QServe.Controllers;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class PaymentWebhookTests
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<IQrCodeService> _qrCodeServiceMock;
    private readonly PaymentController _controller;

    public PaymentWebhookTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _paymentServiceMock = new Mock<IPaymentService>();
        _qrCodeServiceMock = new Mock<IQrCodeService>();

        _controller = new PaymentController(_db, _paymentServiceMock.Object, _qrCodeServiceMock.Object);
    }

    [Fact]
    public async Task RazorpayWebhook_MissingSignatureHeader_ReturnsBadRequest()
    {
        var httpContext = new DefaultHttpContext();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"event\":\"payment.captured\"}");
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length;

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.RazorpayWebhook();

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Missing X-Razorpay-Signature header.", badRequestResult.Value);
    }

    [Fact]
    public async Task RazorpayWebhook_InvalidSignature_ReturnsUnauthorized()
    {
        var httpContext = new DefaultHttpContext();
        var jsonBody = "{\"event\":\"payment.captured\",\"payload\":{}}";
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length;
        httpContext.Request.Headers["X-Razorpay-Signature"] = "invalid_signature_hex";

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        _paymentServiceMock
            .Setup(p => p.HandleWebhookAsync(jsonBody, "invalid_signature_hex"))
            .ReturnsAsync(false);

        var result = await _controller.RazorpayWebhook();

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RazorpayWebhook_ValidSignature_ReturnsOk()
    {
        var httpContext = new DefaultHttpContext();
        var jsonBody = "{\"event\":\"payment.captured\",\"payload\":{\"payment\":{\"entity\":{\"id\":\"pay_123\"}}}}";
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length;
        httpContext.Request.Headers["X-Razorpay-Signature"] = "valid_signature_hex";

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        _paymentServiceMock
            .Setup(p => p.HandleWebhookAsync(jsonBody, "valid_signature_hex"))
            .ReturnsAsync(true);

        var result = await _controller.RazorpayWebhook();

        Assert.IsType<OkResult>(result);
    }
}
