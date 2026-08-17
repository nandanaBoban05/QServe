# PRD — Module 6: AI Order Prioritisation

**Depends on:** Module 1 (Database), Module 4 (Order/OrderItems data)
**Consumed by:** Kitchen Display (queue ordering)
**Estimated effort:** 1 week

## 1. Objective
Classify every order into a priority tier (Quick / Regular / Heavy) using a deterministic rule-based classifier, so a table that only ordered a juice isn't stuck behind a full multi-course meal in the kitchen queue.

Note: this is a **rule-based classifier**, not a trained ML model — the proposal frames it as "Artificial Intelligence / Expert Systems" in the academic category sense (a rule-based expert system), not machine learning. Implement it as straightforward, testable business logic.

## 2. Scope
### In Scope
- `ClassifyOrder` logic run at order-approval time
- `Orders.OrderType` field set to Quick / Regular / Heavy
- `BuildPriorityQueue` logic that sorts approved orders for kitchen display

### Out of Scope
- Any ML model, training pipeline, or historical-data-driven classification (explicitly out of scope per proposal — "rule-based," not ML)

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| AI-1 | When an order is approved, count `OrderItems` whose `MenuItems.ItemType` is `'Cooked'` or `'Dessert'` |
| AI-2 | If that count = 0 → `OrderType = 'Quick'` |
| AI-3 | If that count is 1 or 2 → `OrderType = 'Regular'` |
| AI-4 | If that count is ≥ 3 → `OrderType = 'Heavy'` |
| AI-5 | The kitchen priority queue sorts: all `Quick` orders first (by `ApprovedAt` ascending), then all `Regular`, then all `Heavy` |
| AI-6 | Classification runs automatically on approval — no manual step |

## 4. Data Model
Reads: `OrderItems`, `MenuItems.ItemType`.
Writes: `Orders.OrderType`.

## 5. Process Logic
```
ClassifyOrder(orderId):
  items = get OrderItems for orderId, joined to MenuItems
  heavyCount = count where ItemType in ('Cooked', 'Dessert')
  if heavyCount == 0: type = 'Quick'
  elif heavyCount <= 2: type = 'Regular'
  else: type = 'Heavy'
  Orders.OrderType = type
  save

BuildPriorityQueue(approvedOrders):
  return approvedOrders
    .OrderBy(priority rank: Quick=0, Regular=1, Heavy=2)
    .ThenBy(ApprovedAt)
```

## 6. Data Structure
```csharp
// OrderPriorityQueue: List<Order> sorted Quick → Regular → Heavy, then FIFO by ApprovedAt
```

## 7. Acceptance Criteria
- [ ] An order of 2 beverages only classifies as `Quick`
- [ ] An order of 1 beverage + 1 cooked dish classifies as `Regular`
- [ ] An order of 3 cooked dishes classifies as `Heavy`
- [ ] In a mixed queue of Heavy orders approved earlier and a Quick order approved later, the Quick order still appears first in the kitchen queue
- [ ] Classification is a pure function of order contents — the same order contents always produce the same classification (fully testable with unit tests, no external dependencies)

## 8. Interfaces Exposed to Other Modules
- `IOrderClassifier.Classify(orderId) → OrderType` — called by Payment Processing at the moment an order transitions to `Approved`
- `IPriorityQueueBuilder.Build(IEnumerable<Order> approvedOrders) → List<Order>` — called by Kitchen Display to render the queue
