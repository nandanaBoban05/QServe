using QServe.Models;

namespace QServe.Services;

public record RazorpayCheckoutInfo(string RazorpayOrderId, string KeyId, long AmountInPaise, string Currency, int OrderId);

public enum ConfirmResult { Approved, SignatureInvalid, OrderNotFound, AlreadyProcessed }

public interface IPaymentService
{
    /// <summary>
    /// PAY-1: Creates a Razorpay order server-side for an Order already sitting in
    /// PendingPayment with PaymentMode=Online (created by Module 4's Checkout).
    /// </summary>
    Task<RazorpayCheckoutInfo> InitiateOnlinePaymentAsync(int orderId);

    /// <summary>
    /// PAY-2/PAY-3: Verifies the HMAC-SHA256 signature Razorpay's checkout.js handler returns
    /// after a successful payment, and — only on a genuine match — approves the order. This is
    /// the primary confirmation path (fires the instant the customer's browser gets a success
    /// response). See HandleWebhookAsync below for the redundant, independent second path.
    /// Never trust the "success" callback alone; this signature check is what makes it safe.
    /// </summary>
    Task<ConfirmResult> ConfirmOnlinePaymentAsync(int orderId, string razorpayOrderId, string razorpayPaymentId, string razorpaySignature);

    /// <summary>PAY-9: mark an order's online payment as failed (Razorpay checkout.js "payment.failed" event).</summary>
    Task MarkOnlinePaymentFailedAsync(int orderId);

    /// <summary>PAY-6/PAY-7/PAY-8: Admin approves or rejects a pending Cash/Card payment.</summary>
    Task AdminVerifyAsync(int paymentId, bool approve, int adminUserId, string? rejectionReason = null);

    /// <summary>
    /// Repayment: Creates a new payment attempt for an existing order after rejection/failure,
    /// recalculating prices server-side and preserving previous payment attempts in history.
    /// </summary>
    Task<Payment> InitiateRepaymentAsync(int orderId, string paymentMode);

    /// <summary>
    /// Gap fix (was flagged in GAP-ANALYSIS.md): a true server-to-server Razorpay webhook,
    /// independent of ConfirmOnlinePaymentAsync. Verifies the raw request body against
    /// X-Razorpay-Signature using a SEPARATE webhook secret (not the checkout signature
    /// scheme) — this is what catches an order that should have been approved but never was,
    /// e.g. because the customer's browser tab closed before the checkout.js callback fired.
    /// Idempotent against ConfirmOnlinePaymentAsync already having approved the same payment.
    /// Returns false if the signature doesn't verify — caller should respond 401, not 200, so
    /// Razorpay's dashboard correctly shows the delivery as failed rather than silently trusted.
    /// </summary>
    Task<bool> HandleWebhookAsync(string rawRequestBody, string signatureHeader);

    /// <summary>
    /// Gap fix: refunds a Received payment. For Online, calls Razorpay's refund API against
    /// the recorded RazorpayPaymentID. For Cash/Card, the actual money movement happens
    /// physically at the till — this just records the state change and audit trail; there's
    /// no gateway call to make for those modes.
    /// </summary>
    Task RefundAsync(int paymentId, int adminUserId);
}
