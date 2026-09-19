namespace QServe.Services;

public interface IQrCodeService
{
    /// <summary>
    /// Builds a signed ordering URL for the given table using existing or initial token salt,
    /// saves it to RestaurantTable.QRCodeData, and returns a PNG-encoded QR image of that URL.
    /// </summary>
    Task<byte[]> GenerateForTableAsync(int tableId);

    /// <summary>
    /// Rotates the table's QrTokenSalt, invalidating all previously printed QR codes for this table,
    /// saves the updated URL to RestaurantTable.QRCodeData, and returns the new PNG-encoded QR image.
    /// </summary>
    Task<byte[]> RegenerateForTableAsync(int tableId);

    /// <summary>
    /// Verifies that a token was genuinely issued for the given table ID by this server
    /// against the current table salt (HMAC signature check).
    /// </summary>
    bool ValidateToken(int tableId, string token);

    /// <summary>
    /// Asynchronously verifies that a token was genuinely issued for the given table ID.
    /// </summary>
    Task<bool> ValidateTokenAsync(int tableId, string token);

    /// <summary>Builds the signed token for a table ID with optional per-table salt.</summary>
    string BuildToken(int tableId, string? salt = null);
}
