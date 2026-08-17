using Microsoft.AspNetCore.SignalR;

namespace QServe.Hubs;

/// <summary>
/// Module 7: the single SignalR hub used across the whole system — Customer Ordering's status
/// page, the Kitchen Display, and the Admin payment verification queue all subscribe to this
/// same connection. Don't create a second hub for a different module; broadcast a differently-
/// named event on this one instead (see IRealtimeNotifier).
///
/// No server-invoked methods are needed yet — clients only listen. Add group-join methods here
/// if you later want to scope broadcasts (e.g., a customer only receiving updates for their own
/// order instead of every order in the restaurant).
/// </summary>
public class KitchenHub : Hub
{
}
