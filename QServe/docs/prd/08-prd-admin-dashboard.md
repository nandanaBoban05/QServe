# PRD — Module 8: Admin Dashboard

**Depends on:** Module 1 (Database), Module 2 (Auth/RBAC), Module 5 (payment verification queue)
**Consumed by:** Reporting & Analytics (shares the dashboard shell)
**Estimated effort:** 3 weeks

## 1. Objective
Give Admin/Manager users a single control surface: manage the menu, verify offline payments, monitor live order flow, and manage tables/staff.

## 2. Scope
### In Scope
- Menu management: CRUD on `MenuCategories` and `MenuItems`, including real-time availability toggle
- Payment verification queue: list pending Cash/Card payments, approve/reject (delegates to Module 5's `AdminVerify`)
- Order monitoring: live view of all active orders and their status (subscribes to Module 7's SignalR broadcast)
- Table management: CRUD on `RestaurantTables`, trigger QR regeneration (Module 3)
- Staff management: CRUD on `Users` (create Kitchen/Admin/Manager accounts, deactivate)
- Landing dashboard with at-a-glance stats (today's orders, revenue, pending verifications)

### Out of Scope
- Detailed report generation (that's Module 10 — this module may link out to it)
- Menu item image upload pipeline details beyond storing an `ImageUrl` (implementer's choice: local storage, blob storage, etc.)

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| ADM-1 | Admin can create, edit, and soft-delete (deactivate) menu categories and items |
| ADM-2 | Admin can toggle `MenuItems.IsAvailable` in real time — reflected on the customer menu within seconds |
| ADM-3 | Admin sees a live-updating list of Cash/Card payments in `AwaitingVerification`, with one-click Approve/Reject |
| ADM-4 | Admin sees a live monitor of all active orders with current status, table, and elapsed time |
| ADM-5 | Admin can add, edit, deactivate restaurant tables, and trigger QR (re)generation |
| ADM-6 | Admin (not Manager or Kitchen) can create/deactivate staff accounts and assign roles |
| ADM-7 | Dashboard landing page shows today's order count, revenue, and count of pending verifications |
| ADM-8 | All admin actions that change state are recorded in `AuditLogs` |
| ADM-9 | All admin routes require `[Authorize(Roles = "Admin,Manager")]`; staff management additionally requires `Admin` specifically |

## 4. Data Model
Reads/Writes: `MenuCategories`, `MenuItems`, `RestaurantTables`, `Users`.
Reads: `Orders`, `Payments` (via Module 5/7 interfaces — do not duplicate their business logic here, call their services).

## 5. Process Logic
```
ToggleAvailability(itemId):
  item = get MenuItem
  item.IsAvailable = !item.IsAvailable
  save
  // Customer menu (Module 4) reflects this on next page load / live if polling is used there

ApprovePendingPayment(paymentId):
  delegate to Module 5's AdminVerify(paymentId, approve=true, currentAdminUserId)

CreateStaffAccount(fullName, email, role):
  require role == Admin (not Manager)
  hash password (BCrypt)
  create Users row
```

## 6. Non-Functional Requirements
- Dashboard should load its core stats within 2 seconds (global NFR).
- Payment verification list should update live (SignalR) so nothing is missed during a rush — Admin shouldn't have to refresh to see a new Cash order waiting.

## 7. Acceptance Criteria
- [ ] Toggling an item's availability off immediately removes it from being orderable on the customer menu
- [ ] A new Cash payment appears in the Admin verification queue without a page refresh
- [ ] Only `Admin` role (not `Manager`) can create a new staff account
- [ ] Deactivating a table breaks new orders against it but doesn't delete historical order data
- [ ] Every menu edit, payment verification, and staff account change appears in `AuditLogs` with the correct `PerformedBy`

## 8. Interfaces Exposed to Other Modules
- Calls `IPaymentService.AdminVerify` (Module 5) rather than reimplementing verification logic.
- Subscribes to `KitchenHub` broadcasts (Module 7) for the live order monitor.
- Calls `IQrCodeService.GenerateForTable` (Module 3) for table QR regeneration.
