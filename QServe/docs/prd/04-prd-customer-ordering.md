# PRD — Module 4: Customer Ordering

**Depends on:** Module 1 (Database), Module 3 (QR token validation)
**Consumed by:** Payment Processing (checkout handoff), AI Prioritisation (order data), Recommendation Engine (menu display)
**Estimated effort:** 3 weeks

## 1. Objective
Let a customer scan a table QR, browse the live menu on their own phone browser, build a cart with customisations, and check out — with zero app install and zero login.

## 2. Scope
### In Scope
- Table-scoped landing page (validates QR token from Module 3)
- Digital menu browse: categories, items, prices, real-time availability
- Cart: add/remove items, adjust quantity, add customisation text (e.g., "no onions, extra spicy")
- Checkout: hand off to Payment Processing (Module 5) with mode selection (Online/Cash/Card)
- Order status tracking page: Pending → Approved → Preparing → Ready → Served, updated live
- Mobile-first responsive Razor Views

### Out of Scope
- Customer accounts/login
- Order history across visits (no persistent customer identity)
- Push notifications (native app only — out of scope for whole project)

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| CUST-1 | Scanning QR opens menu instantly in any smartphone browser — no install |
| CUST-2 | Menu is grouped by `MenuCategories`, ordered by `DisplayOrder` |
| CUST-3 | Only `IsAvailable = 1` items are orderable; unavailable items show as greyed out, not orderable |
| CUST-4 | Customer can add items to cart, adjust quantity, and attach free-text customisation per line item |
| CUST-5 | Cart total recalculates live as items are added/changed |
| CUST-6 | Checkout requires selecting a payment mode: Online (UPI/Card via Razorpay), Cash, or Physical POS Card |
| CUST-7 | On checkout submit, an `Order` row is created with `OrderStatus = 'PendingPayment'` and `OrderItems` snapshot current prices into `UnitPrice` |
| CUST-8 | Customer sees a live-updating status: Pending → Approved → Preparing → Ready → Served |
| CUST-9 | Interface must be usable with zero training — clear labels, large tap targets, no jargon |

## 4. Data Model
Reads: `MenuCategories`, `MenuItems`, `RestaurantTables`.
Writes: `Orders` (new row, status `PendingPayment`), `OrderItems` (one row per cart line).
Handoff: `Orders.OrderID` passed to Payment Processing module.

## 5. Process Logic (Activity Flow)
```
ScanQR → validate token (Module 3) → BrowseMenu → AddToCart → Checkout
  → SelectPayment (Online | Cash | Card)
  → create Order (status=PendingPayment) + OrderItems (price snapshot)
  → hand off OrderID to Payment Processing module
  → [Payment module drives status forward from here]
  → customer polls/subscribes to order status until Served
```

## 6. Real-Time Requirements
- Order status page should update live without manual refresh. This module's view subscribes to the SignalR hub owned by the Kitchen Display module (Module 7) — do not build a second hub; reuse the existing `OrderStatusUpdate` broadcast: `{ OrderID, TableNumber, NewStatus, UpdatedAt }`.

## 7. Non-Functional Requirements
- Mobile-first responsive design — most traffic is phone browsers on restaurant Wi-Fi.
- Page must render usably even on a slow/flaky connection (minimize payload, avoid heavy JS frameworks if plain Razor + light JS suffices).
- Response times < 2 seconds under normal load (global NFR).

## 8. Acceptance Criteria
- [ ] A customer with no app installed can go from QR scan to placed order in under 5 taps for a simple order
- [ ] Cart total always matches the sum of line items at current snapshot prices
- [ ] Attempting to order an unavailable item is blocked client- and server-side
- [ ] Order status updates on the customer's screen within a few seconds of a kitchen/admin status change, without the customer refreshing
- [ ] Table-scoping is enforced — a customer cannot place an order against a table they didn't scan (see Module 3 token validation)

## 9. Interfaces Exposed to Other Modules
- `Order` created with `OrderID`, `TableID`, `OrderStatus='PendingPayment'`, `OrderItems[]` — Payment Processing module picks up from here.
- Consumes SignalR broadcast `OrderStatusUpdate` from the Kitchen Display module for live status.
