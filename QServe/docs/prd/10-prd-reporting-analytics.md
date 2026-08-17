# PRD — Module 10: Reporting & Analytics

**Depends on:** Module 1 (Database), Module 5 (payment data), Module 8 (dashboard shell to host reports)
**Estimated effort:** 1.5 weeks

## 1. Objective
Give Admin/Manager users the operational reports needed to run the restaurant: sales, popular items, kitchen throughput, payment verification history, and table utilisation.

## 2. Scope
### In Scope
- Six reports as specified below, each with on-demand generation and, where noted, a natural trigger cadence
- Simple filterable views (date range at minimum) inside the Admin Dashboard
- Export to CSV/PDF is a reasonable stretch goal but not required for v1 acceptance

### Out of Scope
- Predictive/forecasting reports (future scope per proposal — Prophet/ARIMA demand forecasting)
- Cross-branch aggregation (single restaurant only, per proposal's out-of-scope list)

## 3. Reports Specification

| Report | Trigger | Key Metrics | Primary Source Tables |
|---|---|---|---|
| Daily Sales Summary | Daily / on-demand | Revenue, order count, avg order value, payment mode split | Orders, Payments |
| Popular Items Report | Weekly / on-demand | Top 10 items by count; top 10 by revenue | OrderItems, MenuItems |
| Kitchen Throughput | Daily | Avg prep time by item type, peak hours, orders completed | Orders (ApprovedAt, ServedAt) |
| Payment Verification Log | On-demand | Admin, timestamp, amount for all offline payments | Payments, Users |
| Order Status Report | On-demand | Orders by status, pending count, cancellation rate | Orders |
| Table Utilisation Report | Weekly | Orders and revenue per table; busiest tables by hour | Orders, RestaurantTables |

## 4. Functional Requirements
| ID | Requirement |
|---|---|
| REP-1 | Each report supports a date-range filter (default: today, or last 7 days for weekly reports) |
| REP-2 | Daily Sales Summary breaks revenue down by `PaymentMode` (Online/Cash/Card) |
| REP-3 | Popular Items pulls from live `MenuItems.TotalOrdered`/order history, not just the recommendation engine's cached score |
| REP-4 | Kitchen Throughput computes avg prep time as `ServedAt - ApprovedAt` (or `ApprovedAt` to `Preparing`→`Ready` transition if that granularity is tracked) grouped by `MenuItems.ItemType` |
| REP-5 | Payment Verification Log lists every offline payment with `VerifiedBy` (joined to `Users.FullName`) and `VerificationTime` — a full accountability trail |
| REP-6 | Order Status Report shows current distribution of orders across all `OrderStatus` values plus a cancellation-rate percentage |
| REP-7 | Table Utilisation Report ranks tables by order count and revenue, with an hour-of-day breakdown |
| REP-8 | All reports are accessible only to `Admin`/`Manager` roles |

## 5. Data Structures
```csharp
// DailySalesSummary (Report DTO)
{ Date, Revenue, OrderCount, AvgValue, ModeBreakdown }
```
(Follow the same DTO pattern — a dedicated report DTO per report — for the other five reports.)

## 6. Process Logic (representative example)
```
GetDailySalesSummary(dateRange):
  orders = Orders where CreatedAt in dateRange and OrderStatus != 'Cancelled'
  revenue = sum(orders.TotalAmount)
  orderCount = count(orders)
  avgValue = revenue / orderCount
  modeBreakdown = group orders by joined Payments.PaymentMode, sum TotalAmount per mode
  return DailySalesSummary { ... }
```

## 7. Non-Functional Requirements
- Reports involving date-range aggregation should still return within the global 2-second target for typical single-day/week ranges; add appropriate DB indexes on `Orders.CreatedAt`, `Payments.CreatedAt` to support this.
- Reports must exclude `Cancelled` orders from revenue figures unless explicitly viewing the Order Status Report (which needs to show cancellations as a metric, not hide them).

## 8. Acceptance Criteria
- [ ] Daily Sales Summary total revenue matches the sum of `Received` payments for the selected date range
- [ ] Popular Items Report ranking matches what a manual query over `OrderItems` would produce
- [ ] Payment Verification Log shows every offline payment with a named verifying admin — no orphaned/unattributed entries
- [ ] Cancelled orders are excluded from revenue-bearing reports but counted correctly in the Order Status Report's cancellation rate
- [ ] Non-Admin/Manager roles cannot access any report route

## 9. Interfaces Exposed to Other Modules
- Report views are hosted within the Admin Dashboard (Module 8) shell/navigation — this module provides controllers/services, Module 8 provides the surrounding layout.
