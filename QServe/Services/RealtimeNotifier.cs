using Microsoft.AspNetCore.SignalR;
using QServe.Hubs;

namespace QServe.Services;

public class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<KitchenHub> _hub;

    public RealtimeNotifier(IHubContext<KitchenHub> hub)
    {
        _hub = hub;
    }

    public async Task BroadcastOrderStatusUpdateAsync(OrderStatusUpdateDto update)
    {
        // 1. Target the specific customer order group
        await _hub.Clients.Group($"Order_{update.OrderID}").SendAsync("OrderStatusUpdate", update);

        // 2. Target staff (Kitchen / Admin / Manager dashboards)
        await _hub.Clients.Group("Staff").SendAsync("OrderStatusUpdate", update);
    }

    public async Task BroadcastPaymentPendingAsync(PaymentPendingDto pending)
    {
        // Staff-only notification for pending cash/card payment verification
        await _hub.Clients.Group("Staff").SendAsync("PaymentPendingVerification", pending);
    }
}
