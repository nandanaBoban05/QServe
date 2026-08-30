using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QServe.Data;

namespace QServe.Services;

public class QrCodeService : IQrCodeService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public QrCodeService(ApplicationDbContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<byte[]> GenerateForTableAsync(int tableId)
    {
        var table = await _db.RestaurantTables.FindAsync(tableId)
            ?? throw new InvalidOperationException($"Table {tableId} not found.");

        var token = BuildToken(tableId);
        var baseUrl = ResolveBaseUrl();
        var url = $"{baseUrl}/order/table/{tableId}?token={Uri.EscapeDataString(token)}";

        table.QRCodeData = url;
        await _db.SaveChangesAsync();

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var pngQr = new PngByteQRCode(qrData);
        return pngQr.GetGraphic(20); // 20px per module -> roughly 500x500px, print-friendly
    }

    public bool ValidateToken(int tableId, string token)
    {
        var table = _db.RestaurantTables.Find(tableId);

        // QR-5: inactive or missing tables never resolve to a working order flow.
        if (table is null || !table.IsActive)
            return false;

        var expectedToken = BuildToken(tableId);

        // Constant-time comparison to avoid timing side-channels on token guessing.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expectedToken));
    }

    public string BuildToken(int tableId)
    {
        var secret = _config["QrCode:SigningSecret"]
            ?? throw new InvalidOperationException("QrCode:SigningSecret is not configured.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(tableId.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Prefers an explicit, non-localhost App:BaseUrl (real production override). Otherwise
    // derives the origin from the current request — this is what makes the QR automatically
    // pick up the Dev Tunnel's HTTPS URL, since UseForwardedHeaders (Program.cs) rewrites
    // Request.Scheme/Request.Host to the tunnel's public values.
    private string ResolveBaseUrl()
    {
        var configured = _config["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return configured.TrimEnd('/');

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null)
            return $"{request.Scheme}://{request.Host}";

        throw new InvalidOperationException("App:BaseUrl is not configured and no active HTTP request is available to derive it from.");
    }
}