# PRD — Module 9: Recommendation Engine

**Depends on:** Module 1 (Database), Module 4 (menu display surface, order data to update counters)
**Consumed by:** Customer Ordering (displays "Popular" badges)
**Estimated effort:** 0.5 weeks

## 1. Objective
Highlight popular menu items to customers using a simple scoring formula over historical + recent order volume — improving discoverability and average order value through lightweight digital upselling.

## 2. Scope
### In Scope
- Score calculation: `Score = (TotalOrdered × 0.7) + (RecentOrdered × 0.3)`
- Top-N item selection for a "Popular" badge on the customer menu
- Counter maintenance: increment `MenuItems.TotalOrdered` and `MenuItems.RecentOrdered` when orders are placed/approved
- A scheduled or on-demand job to decay `RecentOrdered` (rolling 7-day window)

### Out of Scope
- Personalised, per-customer recommendations (explicitly out of scope per proposal — no customer identity exists)
- Machine learning / collaborative filtering (future scope per proposal)

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| REC-1 | `TotalOrdered` increments by quantity ordered whenever an `OrderItem` is created (cumulative, all-time) |
| REC-2 | `RecentOrdered` increments the same way but reflects only the trailing 7 days |
| REC-3 | Score = `(TotalOrdered × 0.7) + (RecentOrdered × 0.3)`, computed per `MenuItem` |
| REC-4 | The top N (e.g., top 5) items by score display a "Popular" badge on the customer menu |
| REC-5 | Only `IsAvailable = 1` items are eligible for the Popular badge — no promoting something customers can't order |
| REC-6 | `RecentOrdered` is recalculated/decayed on a schedule (e.g., nightly job re-deriving it from the last 7 days of `OrderItems`, rather than an unbounded incrementing counter) |

## 4. Data Model
Reads/Writes: `MenuItems.TotalOrdered`, `MenuItems.RecentOrdered`.
Reads: `OrderItems`, `Orders.CreatedAt` (for the 7-day window recalculation).

## 5. Process Logic
```
OnOrderApproved(order):
  for each OrderItem in order:
    MenuItems.TotalOrdered += OrderItem.Quantity
    // RecentOrdered handled by nightly recompute, not incremented live, to avoid drift

NightlyRecomputeRecentOrdered():
  for each MenuItem:
    RecentOrdered = SUM(OrderItems.Quantity)
      WHERE ItemID = MenuItem.ItemID
      AND Order.CreatedAt >= (today - 7 days)
      AND Order.OrderStatus NOT IN ('Cancelled')

GetPopularItems(topN = 5):
  return MenuItems
    .Where(IsAvailable == true)
    .OrderByDescending(Score = TotalOrdered*0.7 + RecentOrdered*0.3)
    .Take(topN)
```

## 6. Acceptance Criteria
- [ ] Placing and approving orders increases `TotalOrdered` for the ordered items by the correct quantity
- [ ] An item with high all-time orders but zero recent orders still scores lower than a currently-trending item, per the weighting formula
- [ ] Unavailable items never show a "Popular" badge even if their score is highest
- [ ] The recommendation calculation is a pure, testable function independent of the UI

## 7. Interfaces Exposed to Other Modules
- `IRecommendationService.GetPopularItems(topN) → List<MenuItem>` — called by Customer Ordering's menu view.
- Hooked into Payment Processing's order-approval event to update `TotalOrdered`.
