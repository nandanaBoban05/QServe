# PRD — Module 1: Database & Core Domain Setup

**Depends on:** none (this is the foundation — build first)
**Consumed by:** every other module

## 1. Objective
Stand up the EF Core code-first data model, migrations, and base domain classes that every other module builds on. Nothing else in the system compiles or runs correctly until this module is done.

## 2. Scope
### In Scope
- EF Core `DbContext` with all 8 core tables
- Code-first entity classes matching the schema below exactly (field names, types, constraints)
- Initial migration + seed data (a handful of sample tables, menu categories, menu items, one admin user)
- Repository pattern base (generic repository + unit of work, OR EF Core `DbContext` injected directly into services — pick one and use it consistently across all modules)
- Connection string configuration for SQL Server (dev) with PostgreSQL as a documented alternative

### Out of Scope
- Any business logic beyond entity relationships and constraints
- Any controllers, views, or UI

## 3. Full Schema (source of truth — all other modules reference this, do not redefine)

### Users
| Field | Type | Constraints |
|---|---|---|
| UserID | int identity | PK |
| FullName | nvarchar(100) | NOT NULL |
| Email | nvarchar(255) | NOT NULL, UNIQUE |
| PasswordHash | nvarchar(512) | NOT NULL |
| Role | nvarchar(20) | NOT NULL, CHECK IN ('Admin','Kitchen','Manager') |
| IsActive | bit | NOT NULL, DEFAULT 1 |
| CreatedAt | datetime2 | NOT NULL, DEFAULT GETDATE() |

### RestaurantTables
| Field | Type | Constraints |
|---|---|---|
| TableID | int identity | PK |
| TableNumber | nvarchar(10) | NOT NULL, UNIQUE |
| QRCodeData | nvarchar(500) | NOT NULL |
| Capacity | int | NOT NULL, DEFAULT 4, CHECK(>0) |
| IsActive | bit | NOT NULL, DEFAULT 1 |

### MenuCategories
| Field | Type | Constraints |
|---|---|---|
| CategoryID | int identity | PK |
| Name | nvarchar(100) | NOT NULL, UNIQUE |
| DisplayOrder | int | NOT NULL, DEFAULT 0 |
| IsActive | bit | NOT NULL, DEFAULT 1 |

### MenuItems
| Field | Type | Constraints |
|---|---|---|
| ItemID | int identity | PK |
| CategoryID | int | FK → MenuCategories, NOT NULL |
| Name | nvarchar(200) | NOT NULL |
| Price | decimal(10,2) | NOT NULL, CHECK(Price > 0) |
| PrepTimeMinutes | int | NOT NULL, DEFAULT 10 |
| ItemType | nvarchar(15) | NOT NULL, CHECK IN ('Beverage','Cooked','Dessert','Quick') |
| IsAvailable | bit | NOT NULL, DEFAULT 1 |
| TotalOrdered | int | NOT NULL, DEFAULT 0 |
| RecentOrdered | int | NOT NULL, DEFAULT 0 |
| ImageUrl | nvarchar(300) | NULL |
| CreatedAt | datetime2 | NOT NULL, DEFAULT GETDATE() |

### Orders
| Field | Type | Constraints |
|---|---|---|
| OrderID | int identity | PK |
| TableID | int | FK → RestaurantTables, NOT NULL |
| OrderStatus | nvarchar(30) | NOT NULL, DEFAULT 'PendingPayment', CHECK IN ('PendingPayment','AwaitingVerification','Approved','Preparing','Ready','Served','Cancelled') |
| OrderType | nvarchar(10) | NOT NULL, DEFAULT 'Regular', CHECK IN ('Quick','Regular','Heavy') |
| TotalAmount | decimal(10,2) | NOT NULL, CHECK(>=0) |
| Notes | nvarchar(500) | NULL |
| CreatedAt | datetime2 | NOT NULL, DEFAULT GETDATE() |
| ApprovedAt | datetime2 | NULL |
| ServedAt | datetime2 | NULL |

### OrderItems
| Field | Type | Constraints |
|---|---|---|
| OrderItemID | int identity | PK |
| OrderID | int | FK → Orders ON DELETE CASCADE, NOT NULL |
| ItemID | int | FK → MenuItems, NOT NULL |
| Quantity | int | NOT NULL, CHECK(Quantity > 0) |
| UnitPrice | decimal(10,2) | NOT NULL (price snapshot at order time) |
| Customization | nvarchar(300) | NULL |
| LineTotal | computed | Quantity * UnitPrice — not stored physically |

### Payments
| Field | Type | Constraints |
|---|---|---|
| PaymentID | int identity | PK |
| OrderID | int | FK → Orders, NOT NULL, UNIQUE (one payment per order) |
| PaymentMode | nvarchar(10) | NOT NULL, CHECK IN ('Online','Cash','Card') |
| PaymentStatus | nvarchar(15) | NOT NULL, DEFAULT 'Pending', CHECK IN ('Pending','Processing','Received','Failed','Refunded') |
| Amount | decimal(10,2) | NOT NULL |
| RazorpayOrderID | nvarchar(100) | NULL |
| RazorpayPaymentID | nvarchar(100) | NULL |
| VerifiedBy | int | FK → Users, NULL |
| VerificationTime | datetime2 | NULL |
| CreatedAt | datetime2 | NOT NULL, DEFAULT GETDATE() |

### AuditLogs
| Field | Type | Constraints |
|---|---|---|
| LogID | bigint identity | PK |
| EntityType | nvarchar(50) | NOT NULL (e.g. "Orders") |
| EntityID | int | NOT NULL |
| Action | nvarchar(100) | NOT NULL (e.g. "StatusChanged", "PaymentVerified") |
| OldValue | nvarchar(max) | NULL — JSON |
| NewValue | nvarchar(max) | NULL — JSON |
| PerformedBy | int | FK → Users, NULL |
| Timestamp | datetime2 | NOT NULL, DEFAULT GETDATE() |

## 4. Entity Relationships
- RestaurantTables 1—* Orders
- MenuCategories 1—* MenuItems
- Orders 1—* OrderItems, MenuItems 1—* OrderItems
- Orders 1—1 Payments
- Users 1—* AuditLogs (PerformedBy), Users 1—* Payments (VerifiedBy)

## 5. Acceptance Criteria
- [ ] `dotnet ef database update` runs cleanly from empty DB and creates all 8 tables with correct constraints
- [ ] All CHECK constraints and FK relationships from the schema above are enforced at the DB level, not just in application code
- [ ] Seed data includes: 1 admin user (Role='Admin'), 5 sample tables, 3 categories, 10 menu items across ItemTypes
- [ ] A second developer can clone the repo, run migrations, and get a working local DB with zero manual steps beyond `dotnet ef database update`

## 6. Notes for Implementer
- Use `IDENTITY(1,1)` (SQL Server) or `SERIAL`/`IDENTITY` (PostgreSQL) — don't hardcode SQL Server–only syntax if PostgreSQL support matters to you; EF Core's provider abstraction handles most of this automatically.
- Every other module PRD assumes these exact table/field names — do not rename anything without updating the shared README.
