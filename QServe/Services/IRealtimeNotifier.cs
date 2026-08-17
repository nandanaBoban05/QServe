using QServe.Hubs;

namespace QServe.Services;

/// <summary>
/// Thin seam over the KitchenHub — keeps PaymentService, CustomerController, etc. decoupled
/// from SignalR specifics. If you ever swap transport (e.g., server-sent events instead of
/// WebSockets), this is the only interface that needs a new implementation.
/// </summary>
public interface IRealtimeNotifier
{
    Task BroadcastOrderStatusUpdateAsync(OrderStatusUpdateDto update);
    Task BroadcastPaymentPendingAsync(PaymentPendingDto pending);
}
