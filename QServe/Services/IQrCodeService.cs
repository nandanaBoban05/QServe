namespace QServe.Services;

public interface IQrCodeService
{
    /// <summary>
    /// Builds a signed ordering URL for the given table, saves it to
    /// RestaurantTable.QRCodeData, and returns a PNG-encoded QR image of that URL.
    /// </summary>
    Task<byte[]> GenerateForTableAsync(int tableId);

    /// <summary>
    /// Verifies that a token was genuinely issued for the given table ID by this server
    /// (HMAC signature check) — used by the Customer Ordering module (Module 4) before
    /// rendering the menu, so table IDs can't just be incremented in the URL bar.
    /// </summary>
    bool ValidateToken(int tableId, string token);

    /// <summary>Builds the signed token for a table ID without regenerating the QR image.</summary>
    string BuildToken(int tableId);
}
