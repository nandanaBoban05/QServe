using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using QServe.Controllers;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using QServe.ViewModels;
using Xunit;

namespace QServe.Tests;

public class AdminReportsControllerTests
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IReportingService> _reportingServiceMock;
    private readonly AdminReportsController _controller;

    public AdminReportsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _reportingServiceMock = new Mock<IReportingService>();
        _controller = new AdminReportsController(_reportingServiceMock.Object, _db);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Role, UserRoles.Admin)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public async Task Sales_ReturnsViewWithDailySalesSummaryDto()
    {
        var expectedSummary = new DailySalesSummaryDto
        {
            Revenue = 15000,
            OrderCount = 30,
            AvgOrderValue = 500,
            RevenueByPaymentMode = new Dictionary<string, decimal> { [PaymentModes.Online] = 15000 }
        };
        _reportingServiceMock
            .Setup(r => r.GetDailySalesSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(expectedSummary);

        var result = await _controller.Sales(null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DailySalesSummaryDto>(viewResult.Model);
        Assert.Equal(15000, model.Revenue);
        Assert.Equal(30, model.OrderCount);
    }

    [Fact]
    public async Task PopularItems_ReturnsViewWithPopularItemsList()
    {
        var byQty = new List<PopularItemDto>
        {
            new() { ItemName = "Butter Chicken", QuantitySold = 45, Revenue = 13500 }
        };
        var byRev = new List<PopularItemDto>
        {
            new() { ItemName = "Butter Chicken", QuantitySold = 45, Revenue = 13500 }
        };
        _reportingServiceMock
            .Setup(r => r.GetPopularItemsReportAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 10))
            .ReturnsAsync((byQty, byRev));

        var result = await _controller.PopularItems(null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<PopularItemDto>>(viewResult.Model);
        Assert.Single(model);
        Assert.Equal("Butter Chicken", model[0].ItemName);
    }

    [Fact]
    public async Task Throughput_ReturnsViewAndSetsPeakHoursViewBag()
    {
        var expectedThroughput = new List<KitchenThroughputDto>
        {
            new() { ItemType = ItemTypes.Cooked, AvgPrepMinutes = 12.0, OrdersCompleted = 25 }
        };
        var expectedPeak = new List<PeakHourDto>
        {
            new() { Hour = 13, OrderCount = 18 }
        };
        _reportingServiceMock
            .Setup(r => r.GetKitchenThroughputAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(expectedThroughput);
        _reportingServiceMock
            .Setup(r => r.GetPeakHoursAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(expectedPeak);

        var result = await _controller.Throughput(null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<KitchenThroughputDto>>(viewResult.Model);
        Assert.Single(model);
        Assert.NotNull(_controller.ViewBag.PeakHours);
    }

    [Fact]
    public async Task PaymentLog_WithDefaultParams_ReturnsPagedResult()
    {
        var sampleLogs = Enumerable.Range(1, 25).Select(i => new PaymentVerificationLogEntryDto
        {
            PaymentId = i,
            TableNumber = $"T{i}",
            Amount = 500,
            PaymentMode = PaymentModes.Cash,
            Approved = true,
            VerifiedByName = "Admin"
        }).ToList();

        _reportingServiceMock
            .Setup(r => r.GetPaymentVerificationLogAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(sampleLogs);

        var result = await _controller.PaymentLog(null, null, page: 1, pageSize: 10);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PagedResult<PaymentVerificationLogEntryDto>>(viewResult.Model);
        Assert.Equal(25, model.TotalCount);
        Assert.Equal(10, model.PageSize);
        Assert.Equal(1, model.Page);
        Assert.Equal(10, model.Items.Count);
    }

    [Fact]
    public async Task PaymentLog_ClampsNegativeAndOversizedPageSize()
    {
        var sampleLogs = Enumerable.Range(1, 5).Select(i => new PaymentVerificationLogEntryDto
        {
            PaymentId = i,
            TableNumber = $"T{i}",
            Amount = 500,
            PaymentMode = PaymentModes.Online,
            Approved = true,
            VerifiedByName = "Admin"
        }).ToList();

        _reportingServiceMock
            .Setup(r => r.GetPaymentVerificationLogAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(sampleLogs);

        // Negative page and pageSize
        var resultNegative = await _controller.PaymentLog(null, null, page: -5, pageSize: -20);
        var viewResultNegative = Assert.IsType<ViewResult>(resultNegative);
        var modelNegative = Assert.IsType<PagedResult<PaymentVerificationLogEntryDto>>(viewResultNegative.Model);
        Assert.Equal(1, modelNegative.Page);
        Assert.Equal(10, modelNegative.PageSize); // clamped to default 10

        // Oversized pageSize (e.g. 500 clamped to 100)
        var resultOversized = await _controller.PaymentLog(null, null, page: 1, pageSize: 500);
        var viewResultOversized = Assert.IsType<ViewResult>(resultOversized);
        var modelOversized = Assert.IsType<PagedResult<PaymentVerificationLogEntryDto>>(viewResultOversized.Model);
        Assert.Equal(100, modelOversized.PageSize);
    }

    [Fact]
    public async Task OrderStatus_ReturnsViewWithOrderStatusReportDto()
    {
        var sampleReport = new OrderStatusReportDto
        {
            TotalOrders = 20,
            CancellationRatePercent = 10.0,
            CountsByStatus = new Dictionary<string, int>
            {
                [OrderStatuses.Served] = 18,
                [OrderStatuses.Cancelled] = 2
            }
        };

        _reportingServiceMock
            .Setup(r => r.GetOrderStatusReportAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(sampleReport);

        var result = await _controller.OrderStatus(null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<OrderStatusReportDto>(viewResult.Model);
        Assert.Equal(20, model.TotalOrders);
        Assert.Equal(10.0, model.CancellationRatePercent);
    }

    [Fact]
    public async Task TableUtilisation_ReturnsViewWithTableUtilisationList()
    {
        var sampleList = new List<TableUtilisationDto>
        {
            new() { TableNumber = "T1", OrderCount = 10, Revenue = 5000 }
        };
        var sampleByHour = new List<TableHourBucketDto>
        {
            new() { TableNumber = "T1", Hour = 12, OrderCount = 3 }
        };

        _reportingServiceMock
            .Setup(r => r.GetTableUtilisationReportAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(sampleList);
        _reportingServiceMock
            .Setup(r => r.GetTableUtilisationByHourAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(sampleByHour);

        var result = await _controller.TableUtilisation(null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<TableUtilisationDto>>(viewResult.Model);
        Assert.Single(model);
        Assert.NotNull(_controller.ViewBag.ByHour);
    }
}
