using Microsoft.AspNetCore.SignalR;
using QServe.Models;
using QServe.Repositories.Interfaces;
using QServe.Services;

namespace QServe.Hubs;

/// <summary>
/// Module 7: SignalR hub used across the system.
/// Protects staff channels and isolates customer order status updates.
/// </summary>
public class KitchenHub : Hub
{
    private readonly IOrderRepository _orderRepository;
    private readonly IQrCodeService _qrCodeService;

    public KitchenHub(IOrderRepository orderRepository, IQrCodeService qrCodeService)
    {
        _orderRepository = orderRepository;
        _qrCodeService = qrCodeService;
    }

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            if (Context.User.IsInRole(UserRoles.Admin) ||
                Context.User.IsInRole(UserRoles.Kitchen) ||
                Context.User.IsInRole(UserRoles.Manager))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "Staff");
            }
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinOrderGroup(int orderId)
    {
        var isStaff = Context.User?.Identity?.IsAuthenticated == true &&
            (Context.User.IsInRole(UserRoles.Admin) ||
             Context.User.IsInRole(UserRoles.Kitchen) ||
             Context.User.IsInRole(UserRoles.Manager));

        if (isStaff)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Order_{orderId}");
            return;
        }

        var httpContext = Context.GetHttpContext();
        if (httpContext?.Session == null)
            return;

        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return;

        if (TableSession.CanAccessOrder(httpContext.Session, _qrCodeService, order.TableID, orderId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Order_{orderId}");
        }
    }

    public async Task LeaveOrderGroup(int orderId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Order_{orderId}");
    }
}
