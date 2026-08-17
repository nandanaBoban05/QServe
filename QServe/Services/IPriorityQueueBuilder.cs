using QServe.Models;

namespace QServe.Services;

public interface IPriorityQueueBuilder
{
    /// <summary>
    /// AI-5: sorts approved orders Quick -> Regular -> Heavy, then FIFO by ApprovedAt within
    /// each tier. Pure in-memory sort — takes whatever set of orders the caller (Kitchen
    /// Display, Module 7) hands it; doesn't query the DB itself.
    /// </summary>
    List<Order> Build(IEnumerable<Order> approvedOrders);
}
