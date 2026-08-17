# PRD — Module 5: Payment Processing

**Depends on:** Module 1 (Database), Module 4 (Order handoff)
**Consumed by:** Kitchen Display (only payment-approved orders are shown), Admin Dashboard (verification queue), Reporting
**Estimated effort:** 2.5 weeks

## 1. Objective
Ensure **no order reaches the kitchen without confirmed payment.** Handle three payment modes with a single unified verification workflow: Online (auto-verified via Razorpay webhook) and Cash/Card (manually verified by Admin).

## 2. Scope
### In Scope
- Razorpay order creation + checkout widget launch for Online payments
- Razorpay webhook receiver with HMAC-SHA256 signature verification
- Payment record creation for all three modes (`Payments` table)
- Admin manual verification workflow for Cash/POS Card payments
- Order status transition on payment outcome (Approved / Cancelled)
- SignalR notification to Admin when a Cash/Card payment needs verification

### Out of Scope
- Refund processing UI (schema supports `Refunded` status; full refund workflow is future scope)
- Split payments / partial payments

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| PAY-1 | On checkout with mode=Online, create a Razorpay order server-side and return checkout params to the client |
| PAY-2 | Razorpay webhook is verified server-side via HMAC-SHA256 (Key_Secret + RazorpayOrderID + RazorpayPaymentID) before trusting it |
| PAY-3 | On verified online success, `Payments.PaymentStatus = 'Received'` and `Orders.OrderStatus` transitions from `PendingPayment` → `Approved` |
| PAY-4 | On checkout with mode=Cash or Card, create a `Payments` row with `PaymentStatus='Pending'`, `Orders.OrderStatus = 'AwaitingVerification'` |
| PAY-5 | Admin receives a live notification (SignalR) when a new offline payment is awaiting verification |
| PAY-6 | Admin can approve or reject a pending Cash/Card payment with one click |
| PAY-7 | Approval sets `PaymentStatus='Received'`, records `VerifiedBy` + `VerificationTime`, moves order to `Approved` |
| PAY-8 | Rejection sets `Orders.OrderStatus = 'Cancelled'` |
| PAY-9 | Gateway failure on Online payment sets `Orders.OrderStatus = 'Cancelled'`, `PaymentStatus='Failed'` |
| PAY-10 | All order + payment status changes happen inside a single atomic DB transaction (no partial state) |

## 4. Data Model
Reads/Writes: `Payments`, `Orders`.
Reads `VerifiedBy` → `Users.UserID` (from Module 2's authenticated Admin).

## 5. Process Logic

### Online Flow
```
CreateRazorpayOrder(orderId, amount):
  call Razorpay API → get RazorpayOrderID
  create Payments row: Mode=Online, Status=Processing, RazorpayOrderID
  return checkout params to client

HandleWebhook(payload, signature):
  recompute HMAC-SHA256(Key_Secret + RazorpayOrderID + RazorpayPaymentID)
  if signature mismatch → reject, log security event
  if match:
    update Payments: Status='Received', RazorpayPaymentID, set VerificationTime
    update Orders: OrderStatus='Approved', ApprovedAt=now
    broadcast OrderStatusUpdate via SignalR (to Kitchen + Customer)
```

### Offline Flow (Cash / POS Card)
```
CreateOfflinePayment(orderId, mode):
  create Payments row: Mode=mode, Status='Pending', Amount
  update Orders: OrderStatus='AwaitingVerification'
  notify Admin via SignalR ("payment desk" channel)

AdminVerify(paymentId, approve: bool, adminUserId):
  payment = get Payments by paymentId
  if approve:
    Payments.Status='Received', VerifiedBy=adminUserId, VerificationTime=now
    Orders.OrderStatus='Approved', ApprovedAt=now
  else:
    Orders.OrderStatus='Cancelled'
  write AuditLogs entry (EntityType='Payments', Action='PaymentVerified'/'PaymentRejected')
  broadcast OrderStatusUpdate via SignalR
```

## 6. Security Requirements
- **Never** trust a client-side "payment succeeded" message — Online payment approval only happens via server-verified webhook.
- HMAC-SHA256 signature check is mandatory on every webhook call; reject anything that fails verification, and log the attempt.
- Razorpay API keys stored in environment variables or Azure Key Vault, never in source control.
- All order+payment state transitions wrapped in a DB transaction to prevent an order reaching "Approved" without a corresponding "Received" payment record.

## 7. Acceptance Criteria
- [ ] An Online payment that completes successfully at Razorpay moves the order to `Approved` automatically within seconds, with zero admin action
- [ ] A forged webhook call with an invalid signature is rejected and does not change any order status
- [ ] A Cash order sits in `AwaitingVerification` until an Admin explicitly approves or rejects it — it never silently becomes `Approved`
- [ ] Rejecting a Cash/Card payment cancels the order and it never reaches the kitchen queue
- [ ] Every payment verification action is recorded in `AuditLogs` with the acting Admin's UserID

## 8. Interfaces Exposed to Other Modules
- Broadcasts `OrderStatusUpdate { OrderID, TableNumber, NewStatus, UpdatedAt }` via SignalR — consumed by Customer Ordering and Kitchen Display.
- Kitchen Display (Module 7) should only ever query/display Orders where `OrderStatus` has passed `Approved` — this module is the sole gate that sets that status.
