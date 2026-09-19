using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using QServe.Data;
using QServe.Models;
using QServe.Services;
using Xunit;

namespace QServe.Tests;

public class QrCodeServiceTests
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly QrCodeService _service;

    public QrCodeServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        var configValues = new Dictionary<string, string?>
        {
            ["QrCode:SigningSecret"] = "super-secret-qr-key-1234567890",
            ["App:BaseUrl"] = "https://restaurant.example.com"
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        _service = new QrCodeService(_db, _config, _httpContextAccessorMock.Object);
    }

    [Fact]
    public void BuildToken_Deterministic_ReturnsSameTokenForSameInputs()
    {
        var token1 = _service.BuildToken(1, "salt-abc");
        var token2 = _service.BuildToken(1, "salt-abc");

        Assert.Equal(token1, token2);
        Assert.False(string.IsNullOrWhiteSpace(token1));
    }

    [Fact]
    public void BuildToken_DifferentSalts_ReturnsDifferentTokens()
    {
        var tokenNoSalt = _service.BuildToken(1, null);
        var tokenSalt1 = _service.BuildToken(1, "salt-1");
        var tokenSalt2 = _service.BuildToken(1, "salt-2");

        Assert.NotEqual(tokenNoSalt, tokenSalt1);
        Assert.NotEqual(tokenSalt1, tokenSalt2);
    }

    [Fact]
    public void BuildToken_EmptyAndNullSalt_ProducesSameLegacyToken()
    {
        var tokenNull = _service.BuildToken(5, null);
        var tokenEmpty = _service.BuildToken(5, "");
        var tokenWhitespace = _service.BuildToken(5, "   ");

        Assert.Equal(tokenNull, tokenEmpty);
        Assert.Equal(tokenNull, tokenWhitespace);
    }

    [Fact]
    public void ValidateToken_ValidTableAndToken_ReturnsTrue()
    {
        var table = new RestaurantTable
        {
            TableID = 1,
            TableNumber = "T1",
            QRCodeData = "data",
            IsActive = true,
            QrTokenSalt = "custom-salt-1"
        };
        _db.RestaurantTables.Add(table);
        _db.SaveChanges();

        var validToken = _service.BuildToken(1, "custom-salt-1");
        var isValid = _service.ValidateToken(1, validToken);

        Assert.True(isValid);
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsFalse()
    {
        var table = new RestaurantTable
        {
            TableID = 2,
            TableNumber = "T2",
            QRCodeData = "data",
            IsActive = true,
            QrTokenSalt = "salt-xyz"
        };
        _db.RestaurantTables.Add(table);
        _db.SaveChanges();

        var isValid = _service.ValidateToken(2, "tampered-token-value");
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateToken_InactiveTable_ReturnsFalseEvenWithCorrectToken()
    {
        var table = new RestaurantTable
        {
            TableID = 3,
            TableNumber = "T3",
            QRCodeData = "data",
            IsActive = false,
            QrTokenSalt = "salt-inactive"
        };
        _db.RestaurantTables.Add(table);
        _db.SaveChanges();

        var validToken = _service.BuildToken(3, "salt-inactive");
        var isValid = _service.ValidateToken(3, validToken);

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateToken_NonExistentTable_ReturnsFalse()
    {
        var isValid = _service.ValidateToken(9999, "any-token");
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidAndInvalid_ReturnsCorrectly()
    {
        var table = new RestaurantTable
        {
            TableID = 4,
            TableNumber = "T4",
            QRCodeData = "data",
            IsActive = true,
            QrTokenSalt = "async-salt"
        };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var validToken = _service.BuildToken(4, "async-salt");
        var isValidAsync = await _service.ValidateTokenAsync(4, validToken);
        var isInvalidAsync = await _service.ValidateTokenAsync(4, "wrong-token");

        Assert.True(isValidAsync);
        Assert.False(isInvalidAsync);
    }

    [Fact]
    public async Task GenerateForTableAsync_UpdatesTableQRCodeData_AndReturnsPngBytes()
    {
        var table = new RestaurantTable
        {
            TableID = 5,
            TableNumber = "T5",
            QRCodeData = "",
            IsActive = true,
            QrTokenSalt = null
        };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var pngBytes = await _service.GenerateForTableAsync(5);

        Assert.NotNull(pngBytes);
        Assert.NotEmpty(pngBytes);
        Assert.Contains("https://restaurant.example.com/order/table/5?token=", table.QRCodeData);
    }

    [Fact]
    public async Task RegenerateForTableAsync_RotatesSalt_AndInvalidatesOldToken()
    {
        var table = new RestaurantTable
        {
            TableID = 6,
            TableNumber = "T6",
            QRCodeData = "",
            IsActive = true,
            QrTokenSalt = "initial-salt"
        };
        _db.RestaurantTables.Add(table);
        await _db.SaveChangesAsync();

        var initialToken = _service.BuildToken(6, "initial-salt");
        Assert.True(_service.ValidateToken(6, initialToken));

        var pngBytes = await _service.RegenerateForTableAsync(6);

        Assert.NotNull(pngBytes);
        Assert.NotEmpty(pngBytes);
        Assert.NotEqual("initial-salt", table.QrTokenSalt);
        Assert.False(string.IsNullOrWhiteSpace(table.QrTokenSalt));

        // Old token must now be rejected
        Assert.False(_service.ValidateToken(6, initialToken));

        // New token must be accepted
        var newToken = _service.BuildToken(6, table.QrTokenSalt);
        Assert.True(_service.ValidateToken(6, newToken));
    }
}
