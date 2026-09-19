using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.Repositories.Interfaces;

namespace QServe.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _db;

    public PaymentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Payment?> GetByIdAsync(int paymentId)
    {
        return await _db.Payments.FirstOrDefaultAsync(p => p.PaymentID == paymentId);
    }

    public async Task<Payment?> GetByIdWithOrderAsync(int paymentId)
    {
        return await _db.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId);
    }

    public async Task<Payment?> GetByIdWithOrderAndTableAsync(int paymentId)
    {
        return await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId);
    }

    public async Task<Payment?> GetByRazorpayOrderIdAsync(string razorpayOrderId)
    {
        return await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(p => p.RazorpayOrderID == razorpayOrderId);
    }

    public async Task<string?> GetPaymentStatusAsync(int paymentId)
    {
        return await _db.Payments
            .Where(p => p.PaymentID == paymentId)
            .Select(p => p.PaymentStatus)
            .FirstOrDefaultAsync();
    }

    public async Task AddAsync(Payment payment)
    {
        await _db.Payments.AddAsync(payment);
    }

    public void Update(Payment payment)
    {
        _db.Payments.Update(payment);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
