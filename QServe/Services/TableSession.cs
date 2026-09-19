namespace QServe.Services;

/// <summary>
/// Validates that the current browser session has a legitimate table QR session and,
/// when checking a specific order, that the order belongs to this session's table.
/// Shared by CustomerController and PaymentController.
/// </summary>
public static class TableSession
{
    public static bool IsTableSessionValid(ISession session, IQrCodeService qr, int tableId)
    {
        var storedToken = session.GetString(TokenKey(tableId));
        return storedToken is not null && qr.ValidateToken(tableId, storedToken);
    }

    public static async Task<bool> IsTableSessionValidAsync(ISession session, IQrCodeService qr, int tableId)
    {
        var storedToken = session.GetString(TokenKey(tableId));
        return storedToken is not null && await qr.ValidateTokenAsync(tableId, storedToken);
    }

    public static bool CanAccessOrder(ISession session, IQrCodeService qr, int tableId, int orderId)
    {
        if (!IsTableSessionValid(session, qr, tableId))
            return false;

        var json = session.GetString(OrdersKey(tableId));
        if (json is null) return false;

        var orderIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(json) ?? new();
        return orderIds.Contains(orderId);
    }

    public static async Task<bool> CanAccessOrderAsync(ISession session, IQrCodeService qr, int tableId, int orderId)
    {
        if (!await IsTableSessionValidAsync(session, qr, tableId))
            return false;

        var json = session.GetString(OrdersKey(tableId));
        if (json is null) return false;

        var orderIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(json) ?? new();
        return orderIds.Contains(orderId);
    }

    public static string TokenKey(int tableId) => $"token:table:{tableId}";
    public static string OrdersKey(int tableId) => $"orders:table:{tableId}";
}
