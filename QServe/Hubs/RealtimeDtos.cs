namespace QServe.Hubs;

/// <summary>
/// Matches the OrderStatusUpdate data structure specified in the original proposal exactly.
/// Broadcast on the "OrderStatusUpdate" event whenever an order's status changes — consumed
/// by the Customer status page and the Kitchen Display.
/// </summary>
public record OrderStatusUpdateDto(int OrderID, string TableNumber, string NewStatus, DateTime UpdatedAt);

/// <summary>
/// Broadcast on the "PaymentPendingVerification" event whenever a Cash/Card order lands in
/// AwaitingVerification — consumed by the Admin payment queue (PAY-5).
/// </summary>
public record PaymentPendingDto(int PaymentID, string TableNumber, string PaymentMode, decimal Amount);
