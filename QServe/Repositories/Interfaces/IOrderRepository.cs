using QServe.Models;

namespace QServe.Repositories.Interfaces;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int orderId);
    Task<Order?> GetByIdWithPaymentsAsync(int orderId);
    Task<Order?> GetByIdWithTableAndPaymentsAsync(int orderId);
    Task<Order?> GetByIdWithDetailsAsync(int orderId);
    Task AddAsync(Order order);
    void Update(Order order);
    Task SaveChangesAsync();
}
