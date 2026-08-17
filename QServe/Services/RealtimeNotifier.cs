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

    public Task BroadcastOrderStatusUpdateAsync(OrderStatusUpdateDto update) =>
        _hub.Clients.All.SendAsync("OrderStatusUpdate", update);

    public Task BroadcastPaymentPendingAsync(PaymentPendingDto pending) =>
        _hub.Clients.All.SendAsync("PaymentPendingVerification", pending);
}
