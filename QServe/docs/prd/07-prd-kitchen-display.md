# PRD — Module 7: Kitchen Display System (KDS)

**Depends on:** Module 1 (Database), Module 5 (payment-approved orders), Module 6 (priority queue)
**Consumed by:** Customer Ordering (status broadcast source), Admin Dashboard (order monitoring)
**Estimated effort:** 2 weeks

## 1. Objective
Give kitchen staff a real-time, priority-sorted queue of paid orders, and let them advance order status (Approved → Preparing → Ready) with live updates pushed to customers and admin — no manual refresh anywhere.

## 2. Scope
### In Scope
- Kitchen-facing view showing only payment-approved orders (`OrderStatus` in `Approved`, `Preparing`, `Ready`), sorted by the priority queue from Module 6
- Status update actions: Approved → Preparing → Ready
- SignalR hub (`KitchenHub`) broadcasting order events to all connected clients (kitchen, admin, customer status pages)
- Per-order elapsed-time display so kitchen staff can spot orders sitting too long

### Out of Scope
- Kitchen inventory / ingredient tracking
- Multi-station routing (e.g., separate grill/dessert stations) — out of scope for v1

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| KDS-1 | KDS shows only orders with `OrderStatus` in `Approved`, `Preparing`, `Ready` — never `PendingPayment` or `AwaitingVerification` |
| KDS-2 | Orders are sorted using the priority queue from Module 6 (Quick → Regular → Heavy, then FIFO) |
| KDS-3 | Each order card shows: table number, order type badge, item list with quantities and customisations, elapsed time since approval |
| KDS-4 | Kitchen staff can mark an order `Preparing` and later `Ready` with a single tap/click each |
| KDS-5 | Status changes broadcast immediately via SignalR to all subscribers (customer status page, admin dashboard) |
| KDS-6 | New approved orders appear on the KDS within a few seconds of approval, with no page refresh |
| KDS-7 | KDS requires Kitchen-role authentication (Module 2) |

## 4. Data Model
Reads: `Orders` (filtered/sorted), `OrderItems`, `MenuItems`, `RestaurantTables`.
Writes: `Orders.OrderStatus` (Preparing, Ready).

## 5. Real-Time Architecture
This module **owns** the SignalR hub used across the system:
```csharp
// KitchenHub
BroadcastNewOrder(order)      // fired when Payment module approves an order
NotifyReady(orderId)          // fired when kitchen marks an order Ready
```
Broadcast payload (`OrderStatusUpdate` DTO), reused by Customer Ordering and Admin Dashboard:
```csharp
{ OrderID, TableNumber, NewStatus, UpdatedAt }
```

## 6. Data Structure
```csharp
// KitchenOrderView (ViewModel)
TableNumber, OrderType, Items[], ElapsedMinutes
```

## 7. Non-Functional Requirements
- Real-time updates over WebSockets (SignalR) — no polling as the primary mechanism, though a polling fallback is acceptable for degraded connections.
- Display must be usable on a kitchen tablet/PC screen at a glance — large text, clear priority colour-coding (e.g., Quick=green, Regular=amber, Heavy=red badge).

## 8. Acceptance Criteria
- [ ] A newly approved order appears on the KDS screen within a few seconds, without refresh
- [ ] Marking an order "Ready" instantly updates the connected customer's status page and the Admin Dashboard order monitor
- [ ] Orders are visually sorted by priority tier, not just by approval time
- [ ] A `PendingPayment` or `AwaitingVerification` order never appears on the KDS under any circumstance
- [ ] Kitchen staff without the Kitchen role cannot access the KDS route

## 9. Interfaces Exposed to Other Modules
- SignalR hub `KitchenHub` — Payment Processing calls `BroadcastNewOrder` on approval; Customer Ordering and Admin Dashboard subscribe to `OrderStatusUpdate`.
- Consumes `IPriorityQueueBuilder` from Module 6.
