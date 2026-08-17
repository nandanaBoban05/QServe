# PRD Index — QR Code–Based Intelligent Self-Ordering and Order Management System

**Project:** Restaurant self-ordering, payment verification & kitchen management system
**Architecture:** ASP.NET Core MVC 8.0, Razor Views, EF Core 8, SignalR, SQL Server/PostgreSQL, Razorpay
**Status:** Fresh build — no existing code

This is the master index for module-wise Product Requirements Documents (PRDs). Each module has its own file, written so it can be handed to a developer (human or AI) as a **standalone, self-contained brief** — they should not need to read the other files to start work, though dependencies are called out explicitly.

## How to use these documents
- Hand a single module file to whoever (or whatever) is implementing that module.
- Each file states its dependencies on other modules — build/stub those first, or mock the interface.
- Shared reference material (full schema, global NFRs, security baseline) is centralized below so it isn't repeated nine times — link back here when needed.
- Suggested build order follows the critical path from the original proposal.

## Module List & Suggested Build Order

| # | Module | File | Depends On | Est. Effort |
|---|--------|------|------------|--------------|
| 1 | Database & Core Domain Setup | `01-prd-database-core.md` | — | (foundation, not in original 9 but required first) |
| 2 | Authentication & RBAC | `02-prd-authentication.md` | Module 1 | 1 week |
| 3 | QR Code Generation | `03-prd-qr-generation.md` | Module 1 | 0.5 weeks |
| 4 | Customer Ordering | `04-prd-customer-ordering.md` | Modules 1, 3 | 3 weeks |
| 5 | Payment Processing | `05-prd-payment-processing.md` | Modules 1, 4 | 2.5 weeks |
| 6 | AI Order Prioritisation | `06-prd-ai-prioritisation.md` | Modules 1, 4 | 1 week |
| 7 | Kitchen Display System (KDS) | `07-prd-kitchen-display.md` | Modules 1, 5, 6 | 2 weeks |
| 8 | Admin Dashboard | `08-prd-admin-dashboard.md` | Modules 1, 2, 5 | 3 weeks |
| 9 | Recommendation Engine | `09-prd-recommendation-engine.md` | Modules 1, 4 | 0.5 weeks |
| 10 | Reporting & Analytics | `10-prd-reporting-analytics.md` | Modules 1, 5, 8 | 1.5 weeks |

Total estimated effort: ~15 weeks of module work fitted into the original 12-week Gantt via parallel tracks (see proposal PERT chart — Reporting and QR Generation run with float).

## Global Reference (applies to every module unless a PRD overrides it)

### Tech Stack
| Layer | Technology | Version |
|---|---|---|
| Frontend | Razor Views (ASP.NET MVC) | ASP.NET 8.0 |
| Backend | ASP.NET Core MVC | 8.0 LTS |
| ORM | Entity Framework Core | 8.x, code-first |
| Real-time | ASP.NET Core SignalR | 8.x |
| Database | SQL Server / PostgreSQL | 2019 / 15.x |
| Payments | Razorpay .NET SDK | Latest |
| QR Codes | QRCoder library | 1.4+ |
| Auth | ASP.NET Core Identity | 8.x |
| Hosting | IIS / Azure App Service | — |

### Global Non-Functional Requirements
- **Performance:** all responses < 2 seconds under normal load.
- **Scalability:** support 100+ concurrent users; horizontal scaling via additional server instances.
- **Reliability:** atomic DB transactions for all order + payment operations.
- **Security baseline:** HTTPS + TLS 1.3, BCrypt password hashing, server-side payment signature verification, anti-forgery tokens on all POST forms, parameterised queries only (EF Core handles this — no raw SQL string concatenation).
- **Usability:** customer interface must be operable by any smartphone user with zero training, no app install.
- **Audit:** every state-changing action on Orders, Payments, MenuItems must write to `AuditLogs`.

### Core Data Model (shared across modules — see each PRD for the subset it owns)
`Users`, `RestaurantTables`, `MenuCategories`, `MenuItems`, `Orders`, `OrderItems`, `Payments`, `AuditLogs`.
Full field-level schema lives in `01-prd-database-core.md` — treat that file as the single source of truth for table structure; other PRDs reference table/field names but do not redefine them.

### Order Status Lifecycle (owned by Payment + Kitchen modules, referenced everywhere)
```
Placed → PendingPayment → [Online: Gateway] → Approved → Preparing → Ready → Served
                        → [Cash/Card: AwaitingVerification] → Admin Approves → Approved → ...
                                                             → Admin Rejects → Cancelled
                        → Gateway Fail → Cancelled
```

### Out of Scope (all modules)
- Third-party delivery platform integration (Zomato, Swiggy)
- Native iOS/Android apps
- ML-based demand forecasting or personalised recommendations
- Multi-branch management, loyalty programme
