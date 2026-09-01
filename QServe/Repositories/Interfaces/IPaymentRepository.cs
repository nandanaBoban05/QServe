using QServe.Models;

namespace QServe.Repositories.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(int paymentId);
    Task<Payment?> GetByIdWithOrderAsync(int paymentId);
    Task<Payment?> GetByIdWithOrderAndTableAsync(int paymentId);
    Task<Payment?> GetByRazorpayOrderIdAsync(string razorpayOrderId);
    Task<string?> GetPaymentStatusAsync(int paymentId);
    Task AddAsync(Payment payment);
    void Update(Payment payment);
    Task SaveChangesAsync();
}
