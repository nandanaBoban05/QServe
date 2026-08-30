using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QServe.Data;
using QServe.Models;
using QServe.Services;

namespace QServe.Controllers;

/// <summary>
/// Module 5: the customer-facing half of Payment Processing — launching the Razorpay
/// checkout widget and handling its signed success callback. The Admin-facing verification
/// queue for Cash/Card lives in AdminController (Module 8 territory, but Module 5's logic).
/// </summary>
[AllowAnonymous]
[Route("payment")]
public class PaymentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentService _paymentService;
    private readonly IQrCodeService _qrCodeService;

    public PaymentController(ApplicationDbContext db, IPaymentService paymentService, IQrCodeService qrCodeService)
    {
        _db = db;
        _paymentService = paymentService;
        _qrCodeService = qrCodeService;
    }

    // Landed on from Module 4's Checkout or Status Pay Now when paymentMode == Online.
    [HttpGet("checkout/{orderId:int}")]
    public async Task<IActionResult> Checkout(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        if (!TableSession.CanAccessOrder(HttpContext.Session, _qrCodeService, order.TableID, orderId))
            return RedirectToAction("InvalidTable", "Customer");

        if (order.OrderStatus != OrderStatuses.PendingPayment)
        {
            // Already resolved (approved/cancelled/served) — nothing to pay, send them to status.
            if (order.OrderStatus == OrderStatuses.Cancelled)
            {
                TempData["StatusError"] = $"Order #{orderId} has been cancelled and is no longer payable.";
            }
            return RedirectToAction("Status", "Customer", new { orderId });
        }

        try
        {
            var checkoutInfo = await _paymentService.InitiateOnlinePaymentAsync(orderId);
            return View(checkoutInfo);
        }
        catch (InvalidOperationException ex)
        {
            TempData["StatusError"] = ex.Message;
            return RedirectToAction("Status", "Customer", new { orderId });
        }
    }

    // Called by the browser JS after Razorpay's checkout widget reports success.
    //
    // Not anti-forgery-token-protected: this endpoint's real defense is the HMAC signature
    // check inside ConfirmOnlinePaymentAsync — a forged call without a valid Razorpay
    // signature is rejected regardless. Wiring a proper anti-forgery token through a fetch()
    // call is straightforward if you'd rather have both layers; flagged here as a known
    // simplification rather than left silently.
    [HttpPost("confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int orderId, string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
    {
        var result = await _paymentService.ConfirmOnlinePaymentAsync(orderId, razorpayOrderId, razorpayPaymentId, razorpaySignature);

        return result switch
        {
            ConfirmResult.Approved or ConfirmResult.AlreadyProcessed => Json(new { success = true }),
            ConfirmResult.SignatureInvalid => Json(new { success = false, error = "Signature verification failed." }),
            ConfirmResult.OrderNotFound => NotFound(),
            _ => BadRequest()
        };
    }

    // Called by the browser JS if Razorpay's checkout widget reports failure/dismissal.
    [HttpPost("failed")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Failed(int orderId)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderID == orderId);
        if (order is null) return NotFound();

        if (!TableSession.CanAccessOrder(HttpContext.Session, _qrCodeService, order.TableID, orderId))
            return Forbid();

        await _paymentService.MarkOnlinePaymentFailedAsync(orderId);
        return Json(new { success = true });
    }

    // Gap fix: a TRUE server-to-server webhook, independent of the browser-driven Confirm
    // action above. Configure this exact URL in Razorpay's dashboard under Settings ->
    // Webhooks, subscribed to at least "payment.captured", with a webhook secret that matches
    // Razorpay:WebhookSecret in appsettings.json (a DIFFERENT secret from Razorpay:KeySecret's
    // checkout-signature usage — don't reuse one for the other).
    //
    // Reads the raw body manually (rather than binding a model) because the whole point of a
    // webhook signature is verifying the EXACT bytes Razorpay sent — any framework
    // deserialize-then-reserialize round trip risks the recomputed signature not matching due
    // to whitespace/property-order differences that don't change the JSON's meaning but do
    // change its bytes.
    [HttpPost("webhook/razorpay")]
    public async Task<IActionResult> RazorpayWebhook()
    {
        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        Request.Body.Position = 0;

        if (!Request.Headers.TryGetValue("X-Razorpay-Signature", out var signatureHeader))
            return BadRequest("Missing X-Razorpay-Signature header.");

        var accepted = await _paymentService.HandleWebhookAsync(rawBody, signatureHeader.ToString());

        // Deliberately 401, not 200, on a bad signature — Razorpay's dashboard should show
        // this delivery as failed, not silently succeeded, so a real misconfiguration (wrong
        // secret) is visible to whoever's watching webhook delivery logs.
        return accepted ? Ok() : Unauthorized();
    }
}
