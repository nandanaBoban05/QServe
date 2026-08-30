using QServe.Models;

namespace QServe.Services;

/// <summary>
/// Server-side validation of allowable state transitions throughout the Order lifecycle.
/// Prevents invalid transitions such as Served -> Preparing or Cancelled -> Approved.
/// </summary>
public static class OrderStatusStateMachine
{
    public static bool CanTransition(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
            return true; // Idempotent same-state check

        return currentStatus switch
        {
            OrderStatuses.PendingPayment => nextStatus is OrderStatuses.Approved or OrderStatuses.Cancelled or OrderStatuses.AwaitingVerification or OrderStatuses.PendingPayment,
            OrderStatuses.AwaitingVerification => nextStatus is OrderStatuses.Approved or OrderStatuses.Cancelled or OrderStatuses.PendingPayment or OrderStatuses.AwaitingVerification,
            OrderStatuses.Approved => nextStatus is OrderStatuses.Preparing or OrderStatuses.Cancelled,
            OrderStatuses.Preparing => nextStatus is OrderStatuses.Ready or OrderStatuses.Cancelled,
            OrderStatuses.Ready => nextStatus is OrderStatuses.Served or OrderStatuses.Cancelled,
            OrderStatuses.Served => nextStatus is OrderStatuses.Cancelled, // e.g. post-serve refund
            OrderStatuses.Cancelled => false, // Terminal state — cannot leave Cancelled
            _ => false
        };
    }

    /// <summary>
    /// Checks if the order is in a pre-kitchen state where the customer can cancel the order.
    /// </summary>
    public static bool CanCustomerCancel(string currentStatus)
    {
        return currentStatus is OrderStatuses.PendingPayment or OrderStatuses.AwaitingVerification;
    }
}
