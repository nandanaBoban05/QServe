using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.Repositories.Interfaces;

namespace QServe.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _db;

    public OrderRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Order?> GetByIdAsync(int orderId)
    {
        return await _db.Orders.FirstOrDefaultAsync(o => o.OrderID == orderId);
    }

    public async Task<Order?> GetByIdWithPaymentsAsync(int orderId)
    {
        return await _db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
    }

    public async Task<Order?> GetByIdWithTableAndPaymentsAsync(int orderId)
    {
        return await _db.Orders
            .Include(o => o.Payments)
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
    }

    public async Task<Order?> GetByIdWithDetailsAsync(int orderId)
    {
        return await _db.Orders
            .Include(o => o.Table)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Item)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
    }

    public async Task AddAsync(Order order)
    {
        await _db.Orders.AddAsync(order);
    }

    public void Update(Order order)
    {
        _db.Orders.Update(order);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
