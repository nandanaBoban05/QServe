using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using QServe.Controllers;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using QServe.ViewModels;
using Xunit;

namespace QServe.Tests;

public class AdminTests
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IQrCodeService> _qrCodeServiceMock = new();
    private readonly Mock<IPaymentService> _paymentServiceMock = new();
    private readonly Mock<IWebHostEnvironment> _envMock = new();
    private readonly IConfiguration _config;
    private readonly AdminController _adminController;
    private readonly AdminMenuController _menuController;
    private readonly AdminStaffController _staffController;

    public AdminTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "App:BaseUrl", "https://qserve.test" },
            { "QrCode:SigningSecret", "test_qr_signing_secret_12345" }
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        _qrCodeServiceMock.Setup(q => q.BuildToken(It.IsAny<int>())).Returns("mock_token_abc");
        _qrCodeServiceMock.Setup(q => q.GenerateForTableAsync(It.IsAny<int>())).ReturnsAsync(new byte[] { 1, 2, 3 });

        var userManager = CreateUserManager(_db);
        _adminController = new AdminController(_db, _qrCodeServiceMock.Object, _paymentServiceMock.Object, _config);
        _menuController = new AdminMenuController(_db, _envMock.Object);
        _staffController = new AdminStaffController(_db, userManager);

        // Setup HttpContext for controllers to support User claims & TempData
        SetupControllerContext(_adminController, "1", "Admin");
        SetupControllerContext(_menuController, "1", "Admin");
        SetupControllerContext(_staffController, "1", "Admin");
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext db)
    {
        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        var passwordHasher = new PasswordHasher<User>();
        var userOptions = new Microsoft.Extensions.Options.OptionsWrapper<IdentityOptions>(new IdentityOptions());

        var userManager = new UserManager<User>(
            userStore,
            userOptions,
            passwordHasher,
            new IUserValidator<User>[] { new UserValidator<User>() },
            new IPasswordValidator<User>[] { new PasswordValidator<User>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            (IServiceProvider)null!,
            Mock.Of<ILogger<UserManager<User>>>());

        userManager.RegisterTokenProvider(TokenOptions.DefaultProvider, new EmailTokenProvider<User>());

        return userManager;
    }

    private void SetupControllerContext(Controller controller, string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
            new(ClaimTypes.Name, "Admin User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("qserve.test");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public async Task Orders_FilteringByStatus_ReturnsMatchingOrdersOnly()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);
        _db.Orders.AddRange(
            new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = 150 },
            new Order { Table = table, OrderStatus = OrderStatuses.Preparing, TotalAmount = 200 },
            new Order { Table = table, OrderStatus = OrderStatuses.Served, TotalAmount = 300 },
            new Order { Table = table, OrderStatus = OrderStatuses.Cancelled, TotalAmount = 50 }
        );
        await _db.SaveChangesAsync();

        var filter = new AdminOrderFilterViewModel { OrderStatus = OrderStatuses.Approved };
        var result = await _adminController.Orders(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminOrderFilterViewModel>(result.Model);
        Assert.Single(model.Orders.Items);
        Assert.Equal(OrderStatuses.Approved, model.Orders.Items[0].OrderStatus);
        Assert.Equal(1, model.Orders.TotalCount);
    }

    [Fact]
    public async Task Orders_ActiveFilter_ReturnsOnlyActiveOrders()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);
        _db.Orders.AddRange(
            new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = 150 },
            new Order { Table = table, OrderStatus = OrderStatuses.Preparing, TotalAmount = 200 },
            new Order { Table = table, OrderStatus = OrderStatuses.Ready, TotalAmount = 100 },
            new Order { Table = table, OrderStatus = OrderStatuses.Served, TotalAmount = 300 },
            new Order { Table = table, OrderStatus = OrderStatuses.Cancelled, TotalAmount = 50 }
        );
        await _db.SaveChangesAsync();

        var filter = new AdminOrderFilterViewModel { OrderStatus = "Active" };
        var result = await _adminController.Orders(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminOrderFilterViewModel>(result.Model);
        Assert.Equal(3, model.Orders.Items.Count);
        Assert.DoesNotContain(model.Orders.Items, o => o.OrderStatus == OrderStatuses.Served);
        Assert.DoesNotContain(model.Orders.Items, o => o.OrderStatus == OrderStatuses.Cancelled);
    }

    [Fact]
    public async Task Orders_Pagination_CalculatesTotalPagesAndSkipsCorrectly()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);
        for (int i = 1; i <= 25; i++)
        {
            _db.Orders.Add(new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = i * 10 });
        }
        await _db.SaveChangesAsync();

        var filter = new AdminOrderFilterViewModel { Page = 2, PageSize = 10 };
        var result = await _adminController.Orders(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminOrderFilterViewModel>(result.Model);
        Assert.Equal(10, model.Orders.Items.Count);
        Assert.Equal(25, model.Orders.TotalCount);
        Assert.Equal(3, model.Orders.TotalPages);
        Assert.True(model.Orders.HasPrevPage);
        Assert.True(model.Orders.HasNextPage);
    }

    [Fact]
    public async Task OrderDetails_ReturnsFullOrderGraphAndTimeline()
    {
        var table = new RestaurantTable { TableNumber = "T5", Capacity = 4, IsActive = true };
        var category = new MenuCategory { Name = "Curries", IsActive = true };
        var item = new MenuItem { Name = "Butter Chicken", Category = category, Price = 300, ItemType = ItemTypes.Cooked };
        _db.RestaurantTables.Add(table);
        _db.MenuCategories.Add(category);
        _db.MenuItems.Add(item);

        var order = new Order
        {
            Table = table,
            OrderStatus = OrderStatuses.Approved,
            TotalAmount = 600,
            Notes = "Extra spicy",
            Payment = new Payment { PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received, Amount = 600, RazorpayPaymentID = "pay_12345" }
        };
        order.OrderItems.Add(new OrderItem { Item = item, Quantity = 2, UnitPrice = 300, Customization = "Spicy" });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _db.AuditLogs.AddRange(
            new AuditLog { EntityType = "Orders", EntityID = order.OrderID, Action = "OnlinePaymentApproved", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
            new AuditLog { EntityType = "Payments", EntityID = order.Payment.PaymentID, Action = "PaymentReceived", Timestamp = DateTime.UtcNow.AddMinutes(-4) }
        );
        await _db.SaveChangesAsync();

        var result = await _adminController.OrderDetails(order.OrderID) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminOrderDetailsViewModel>(result.Model);
        Assert.Equal(order.OrderID, model.Order.OrderID);
        Assert.Equal("Butter Chicken", model.Order.OrderItems.First().Item?.Name);
        Assert.Equal(600, model.Order.OrderItems.First().LineTotal);
        Assert.Equal(2, model.Timeline.Count);
        Assert.Equal("OnlinePaymentApproved", model.Timeline[0].Action);
    }

    [Fact]
    public async Task Payments_FilteringByStatusAndAmountRange_ReturnsFilteredResults()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        _db.RestaurantTables.Add(table);
        var order1 = new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = 500 };
        var order2 = new Order { Table = table, OrderStatus = OrderStatuses.PendingPayment, TotalAmount = 150 };
        var order3 = new Order { Table = table, OrderStatus = OrderStatuses.Cancelled, TotalAmount = 1000 };
        _db.Payments.AddRange(
            new Payment { Order = order1, Amount = 500, PaymentMode = PaymentModes.Online, PaymentStatus = PaymentStatuses.Received },
            new Payment { Order = order2, Amount = 150, PaymentMode = PaymentModes.Cash, PaymentStatus = PaymentStatuses.Pending },
            new Payment { Order = order3, Amount = 1000, PaymentMode = PaymentModes.Card, PaymentStatus = PaymentStatuses.Refunded }
        );
        await _db.SaveChangesAsync();

        var filter = new AdminPaymentFilterViewModel
        {
            PaymentStatus = PaymentStatuses.Received,
            MinAmount = 200,
            MaxAmount = 800
        };
        var result = await _adminController.Payments(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminPaymentFilterViewModel>(result.Model);
        Assert.Single(model.Payments.Items);
        Assert.Equal(500, model.Payments.Items[0].Amount);
        Assert.Equal(PaymentStatuses.Received, model.Payments.Items[0].PaymentStatus);
    }

    [Fact]
    public async Task Tables_SearchAndStatusFilter_GeneratesSafeQrUrls()
    {
        _db.RestaurantTables.AddRange(
            new RestaurantTable { TableNumber = "101", Capacity = 2, IsActive = true },
            new RestaurantTable { TableNumber = "102", Capacity = 4, IsActive = false },
            new RestaurantTable { TableNumber = "201", Capacity = 6, IsActive = true }
        );
        await _db.SaveChangesAsync();

        var filter = new AdminTableFilterViewModel { Search = "10", Status = "Active" };
        var result = await _adminController.Tables(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminTableFilterViewModel>(result.Model);
        Assert.Single(model.Tables.Items);
        Assert.Equal("101", model.Tables.Items[0].TableNumber);
        Assert.Contains("mock_token_abc", model.TableQrUrls[model.Tables.Items[0].TableID]);
        Assert.DoesNotContain("test_qr_signing_secret_12345", model.TableQrUrls[model.Tables.Items[0].TableID]);
    }

    [Fact]
    public async Task Menu_CreateItem_RejectsInvalidPriceAndEmptyName()
    {
        var category = new MenuCategory { Name = "Drinks", IsActive = true };
        _db.MenuCategories.Add(category);
        await _db.SaveChangesAsync();

        var invalidItem = new MenuItem { Name = "", Price = -10, CategoryID = category.CategoryID };
        var result = await _menuController.CreateItem(invalidItem, null) as ViewResult;

        Assert.NotNull(result);
        Assert.False(_menuController.ModelState.IsValid);
        Assert.True(_menuController.ModelState.ContainsKey("Price"));
        Assert.True(_menuController.ModelState.ContainsKey("Name"));
    }

    [Fact]
    public async Task Menu_FilterByCategoryAndAvailability_ReturnsCorrectItems()
    {
        var cat1 = new MenuCategory { Name = "Food", IsActive = true };
        var cat2 = new MenuCategory { Name = "Drinks", IsActive = true };
        _db.MenuCategories.AddRange(cat1, cat2);
        await _db.SaveChangesAsync();

        _db.MenuItems.AddRange(
            new MenuItem { Name = "Naan", CategoryID = cat1.CategoryID, Price = 40, IsAvailable = true },
            new MenuItem { Name = "Roti", CategoryID = cat1.CategoryID, Price = 30, IsAvailable = false },
            new MenuItem { Name = "Lassi", CategoryID = cat2.CategoryID, Price = 80, IsAvailable = true }
        );
        await _db.SaveChangesAsync();

        var filter = new AdminMenuFilterViewModel
        {
            CategoryId = cat1.CategoryID,
            Availability = "Available"
        };
        var result = await _menuController.Index(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminMenuFilterViewModel>(result.Model);
        Assert.Single(model.Items.Items);
        Assert.Equal("Naan", model.Items.Items[0].Name);
    }

    [Fact]
    public async Task Staff_SelfDeactivation_IsBlocked()
    {
        var adminUser = new User
        {
            FullName = "Super Admin",
            Email = "admin@qserve.test",
            PasswordHash = "hash123",
            Role = UserRoles.Admin,
            IsActive = true
        };
        _db.Users.Add(adminUser);
        await _db.SaveChangesAsync();

        // Admin 1 tries to deactivate account 1 while logged in
        var result = await _staffController.ToggleActive(adminUser.UserID) as RedirectToActionResult;

        Assert.NotNull(result);
        var userInDb = await _db.Users.FindAsync(adminUser.UserID);
        Assert.NotNull(userInDb);
        Assert.True(userInDb.IsActive); // Remains active
        Assert.NotNull(_staffController.TempData["StaffMessage"]);
    }

    [Fact]
    public async Task AuditLog_FilteringByEntityTypeAndSearch_ReturnsLogs()
    {
        var user = new User { FullName = "Chef John", Email = "chef@qserve.test", PasswordHash = "hash", Role = UserRoles.Kitchen };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.AuditLogs.AddRange(
            new AuditLog { EntityType = "Orders", EntityID = 101, Action = "OrderPlaced", PerformedBy = user.UserID },
            new AuditLog { EntityType = "Orders", EntityID = 102, Action = "OrderCancelled", PerformedBy = user.UserID },
            new AuditLog { EntityType = "Payments", EntityID = 50, Action = "PaymentReceived" },
            new AuditLog { EntityType = "Users", EntityID = 1, Action = "StaffAccountCreated" }
        );
        await _db.SaveChangesAsync();

        var filter = new AdminAuditLogFilterViewModel { EntityType = "Orders", Search = "OrderCancelled" };
        var result = await _adminController.AuditLog(filter) as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<AdminAuditLogFilterViewModel>(result.Model);
        Assert.Single(model.Logs.Items);
        Assert.Equal("OrderCancelled", model.Logs.Items[0].Action);
        Assert.Equal(102, model.Logs.Items[0].EntityID);
    }

    [Fact]
    public async Task CancelOrder_WhenAllowed_CancelsOrderAndLogsAudit()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var order = new Order { Table = table, OrderStatus = OrderStatuses.Approved, TotalAmount = 250 };
        _db.RestaurantTables.Add(table);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _adminController.CancelOrder(order.OrderID, "Customer walked out") as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal(nameof(AdminController.OrderDetails), result.ActionName);

        var dbOrder = await _db.Orders.FindAsync(order.OrderID);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatuses.Cancelled, dbOrder.OrderStatus);

        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.EntityType == "Orders" && a.EntityID == order.OrderID && a.Action == "OrderCancelledByStaff");
        Assert.NotNull(audit);
        Assert.Contains("Customer walked out", audit.NewValue);
    }

    [Fact]
    public async Task CancelOrder_WhenAlreadyCancelled_SetsErrorInTempData()
    {
        var table = new RestaurantTable { TableNumber = "T1", IsActive = true };
        var order = new Order { Table = table, OrderStatus = OrderStatuses.Cancelled, TotalAmount = 250 };
        _db.RestaurantTables.Add(table);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _adminController.CancelOrder(order.OrderID, "Double cancel test") as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal(nameof(AdminController.OrderDetails), result.ActionName);
        Assert.NotNull(_adminController.TempData["OrderDetailsError"]);
    }
}