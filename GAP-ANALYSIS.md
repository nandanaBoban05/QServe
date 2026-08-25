# Gap Analysis — What's Missing or Incomplete, Module by Module

This is an honest audit of the project against the 11 PRD files, done the same way the PRDs
were written — so it can be worked module-wise, same as the original build order. The project
itself was renamed from an internal working name to **QServe** partway through this log; that
rename doesn't get its own module entry below since it's not a PRD-tracked feature, but it's
noted in `GETTING_STARTED.md`.

**Last audited:** August 2026 — verified with `dotnet build` (succeeds, 2 warnings) and
`dotnet test` (15/15 pass).

Status markers:

- ✅ **Done** — matches the PRD, no known gap
- ⚠️ **Partial** — implemented but simplified, deviated, or has a known rough edge
- ❌ **Missing** — not implemented at all

Severity markers (within each issue):

- 🔴 **Critical** — security risk or blocks core functionality in any environment
- 🟠 **High** — significant gap that will bite in dev/staging/production soon
- 🟡 **Medium** — real limitation, safe to defer briefly
- 🟢 **Low** — polish, docs, or scale-at-the-margin items

Read this alongside each module's "notes" section in `GETTING_STARTED.md` — this document is
the checklist; that one has the reasoning behind each call.

---

## 0. Foundational — blocks everything below

✅ **The project builds cleanly.** `dotnet restore` and `dotnet build` succeed against the
current code (two CS8981 warnings on the migration class name `initial` — cosmetic only).

✅ **Initial migration exists.** `Migrations/20260817025447_initial.cs` covers the full schema
including Module 2's additive `User` columns (`AccessFailedCount`, `LockoutEnd`,
`PasswordResetTokenHash`, `PasswordResetTokenExpiry`). Run `dotnet ef database update` on each
new machine to apply it.

✅ **Unit tests pass.** 15 tests across `OrderClassifierTests`, `PriorityQueueBuilderTests`, and
`RecommendationServiceTests` — all pure business logic, no DB/HTTP.

❌ 🔴 **Rotate credentials if they were ever committed.** Committed `appsettings.json` now
uses placeholders only. Copy `appsettings.Development.json.example` →
`appsettings.Development.json` for local secrets. If real Gmail/Razorpay credentials were
previously committed, rotate them in git history.

✅ **`App:BaseUrl` aligned** with `launchSettings.json` (`https://localhost:54905`).

✅ **LocalDB as default connection string** in committed `appsettings.json`; override per machine
in `appsettings.Development.json`.

✅ **`appsettings.Development.json.example`** added — copy to gitignored Development file for
local overrides.

✅ **`wwwroot/images/logo.png`** added.

✅ **Production error handler** — `HomeController.Error` + `Views/Home/Error.cshtml`.

✅ **`/health` endpoint** for load-balancer probes.

⚠️ 🟢 **Legacy NuGet reference.** `Microsoft.AspNetCore.SignalR` v1.2.0 in `QServe.csproj` is
redundant on .NET 8 (SignalR is in the shared framework). Build succeeds; safe to remove.

---

## Module 1 — Database & Core Domain

✅ Schema matches the PRD's 8 tables with constraints and seed data. Initial migration includes
all current columns.

⚠️ **Schema drift from PRD doc.** Four additive columns on `User` (lockout + password-reset
fields) are in the migration but not reflected in `01-prd-database-core.md`. Update the PRD if
you want the doc to stay authoritative.

⚠️ 🟡 **Seed QR URLs are placeholders.** Seeded `RestaurantTables.QRCodeData` uses
`https://localhost/order/table/{id}` until an admin triggers QR generation via Module 3.

⚠️ 🟡 **Default seed passwords are hardcoded** in `ApplicationDbContext` (`Admin123!`,
`Kitchen123!`, `Manager123!`). Fine for local dev only — change before any shared/staging deploy.

⚠️ 🟢 **Outdated seed comment** still says *"Replace with a real BCrypt hash once Module 2 is
implemented"* — hashes are real BCrypt now; comment is stale.

---

## Module 2 — Authentication & RBAC

✅ Login, role-based `[Authorize]`, BCrypt hashing, 5-attempt/15-minute lockout.

✅ **Admin unlock (`ClearLockout`) and admin-assisted password reset (`ResetPassword`) — built.**

✅ **Self-service Forgot Password — built.** `AccountController`'s `ForgotPassword`/`ResetPassword`
actions, backed by `IPasswordResetService` — hashed, single-use, 1-hour-expiry tokens,
enumeration-safe messaging.

✅ **Staff account edit (`Edit`) — built.** Name/email/role changed in place; preserves
`AuditLogs.PerformedBy` and `Payments.VerifiedBy` history.

✅ **`AdminStaffController` writes to `AuditLogs`** for every action.

✅ **Migration includes all auth columns** — see Module 0; no separate migration needed.

✅ **Email sender auto-selects implementation.** `Program.cs` uses `GmailEmailSender` when
`EmailConfig:SenderPassword` is set; `DevEmailSender` (log-only) when placeholders remain.

❌ 🔴 **Rotate credentials if previously committed** — see Module 0.

⚠️ **Custom cookie auth instead of ASP.NET Core Identity** — functional equivalent, conscious
deviation from the proposed tech stack (no 2FA, external login, etc.).

✅ **Admin cannot deactivate themselves or the last active Admin** — guarded in `ToggleActive`.

❌ 🟡 **No rate limiting** on login or forgot-password endpoints — brute-force / email-spam
possible.

---

## Module 3 — QR Code Generation

✅ HMAC-signed per-table token, PNG generation, admin download/regenerate, `IsActive` gating.

✅ Matches PRD scope — dynamic per-scan rotation was explicitly out of scope.

❌ 🟠 **QR URLs depend on `App:BaseUrl`** — wrong port/host in dev breaks scanned links (see
Module 0).

❌ 🟡 **`QrCode:SigningSecret` is still a placeholder** — must be replaced before deployment;
rotating it invalidates every table's QR at once (by design).

⚠️ Never tested against a real phone camera — encoding, URL length, and scan reliability
unverified.

⚠️ 🟢 **Regenerate QR does not change the token** — tokens are deterministic HMAC(tableId);
only secret rotation produces a different code. Documented, but easy to misunderstand.

---

## Module 4 — Customer Ordering

✅ Menu browse, session cart, checkout handoff, live status, re-order flow.

✅ **Cart total recalculates live, client-side** via `data-price` attributes in `Cart.cshtml`.

✅ **Menu item images render** when `ImageUrl` is set (`Menu.cshtml`, with `onerror` fallback).

✅ **Server-side quantity cap** (`MaxLineQuantity = 50`) in `AddToCart` and `UpdateCart`.

✅ **Order status and payment checkout are table-scoped.** Status pages, status JSON, and payment
checkout require a valid table QR session and that the order was placed in this browser session
(`TableSession` helper).

❌ 🟡 **Session cart uses in-memory distributed cache** — cart and table session are lost on app
restart or in a multi-instance deployment without a shared session store (Redis, SQL Server, etc.).

⚠️ 🟢 **`MaxLineQuantity = 50`** is a sanity cap, not a business rule — may need raising for
catering/bulk orders.

---

## Module 5 — Payment Processing

✅ Razorpay order creation, signature-verified checkout confirmation, admin verification queue,
idempotency guards, full audit trail.

✅ **True server-to-server webhook — built.** `POST /payment/webhook/razorpay` with separate
`WebhookSecret`, idempotent against the browser-driven confirm path, both funnel through
`ApproveOnlinePaymentAsync`.

✅ **Refunds — built.** `RefundAsync` calls Razorpay for Online; Cash/Card records state only.
Exposed via `/Admin/Payments` (separate from the active-orders monitor).

❌ 🔴 **Razorpay keys are placeholders** — online payments cannot work until real test/live keys
are configured outside source control.

❌ 🟠 **Never tested against real Razorpay test-mode keys** — checkout, confirm, webhook, and
refund flows are code-complete but unverified end-to-end.

✅ **`POST /payment/failed` protected with CSRF** and table-session validation.

⚠️ **`POST /payment/confirm` has no anti-forgery token** — mitigated by HMAC signature check;
documented as a deliberate simplification.

❌ 🟡 **Webhook not registered in Razorpay dashboard** — server-to-server fallback won't fire
until configured with a real secret.

⚠️ **REST calls instead of the official Razorpay .NET SDK** — documented deviation; more
maintenance burden.

❌ 🟡 **Refund does not update order status** — only sets `PaymentStatus = Refunded`; the order
may still show as `Served` with no linked cancellation/refund state on the order row.

⚠️ 🟢 **Refund list capped at 100 payments, no pagination.**

---

## Module 6 — AI Order Prioritisation

✅ Fully implemented and unit-tested against the PRD's exact acceptance criteria, including
boundary cases (2 vs. 3 heavy items) and the "same input → same output" purity check.

No known functional gaps.

---

## Module 7 — Kitchen Display System

✅ SignalR hub, priority-sorted queue, Preparing/Ready/Served actions, live broadcasts to
Kitchen/Status/Payment Queue screens.

✅ **Kitchen status transitions validated** — Approved→Preparing→Ready→Served enforced server-side.

❌ 🟠 **SignalR hub is unauthenticated.** Any client can connect to `/hubs/kitchen` and receive
all order/payment broadcasts (table numbers, statuses).

❌ 🟡 **`Clients.All` broadcasts to every connection** — no group-based scoping by table, role,
or station. Fine at small scale; won't hold under the PRD's "100+ concurrent users" NFR.

❌ **The "100+ concurrent users, <2s response" NFR has never been load-tested.**

⚠️ **`MarkServed` was added beyond the PRD's strict scope** (kitchen only owns
Approved→Preparing→Ready) to close the demo loop — a real deployment might want that action on
a separate front-of-house screen instead of the kitchen tablet.

❌ No multi-station routing (explicitly out of scope in the PRD — just noting it's still absent).

⚠️ 🟢 **SignalR client JS loaded from external CDN** — offline/air-gapped environments won't
get live updates.

---

## Module 8 — Admin Dashboard

✅ Dashboard stats, live order monitor, table/menu/category CRUD, staff account management,
role-gated correctly (Admin-only staff controller).

✅ **`CreateTable` shows a real error on duplicate table number** via `TempData`.

✅ **Staff account edit, audit log viewer, upgraded dashboard** (7-day Chart.js trend, recent
activity feed, quick actions), **sidebar navigation** via `_AdminNav.cshtml`.

❌ **No image upload pipeline** — `ImageUrl` is still a plain text field in menu item forms;
no file upload, local disk, or blob storage. Displaying a URL (Module 4) is solved; *getting*
a URL is not.

❌ 🟡 **UI never validated in a real browser** — sidebar, dashboard chart, responsive collapse
at 860px, Chart.js dual-axis rendering, and contrast all unverified.

⚠️ 🟡 **Chart.js loaded from external CDN** — requires network access.

⚠️ 🟢 **Audit log pagination is basic** — page number only; no total-pages UI beyond prev/next.

⚠️ 🟢 **Payments/refund screen limited to 100 rows** — will miss older refundable payments at
scale.

---

## Module 9 — Recommendation Engine

✅ Scoring formula, popularity counters wired into both payment-approval paths, nightly
recompute via `RecentOrderedRecalculationService`, "Popular" badges on the customer menu.

No known functional gaps.

⚠️ 🟡 **No persisted "last run" timestamp** — app restart resets the 24-hour recompute
interval (minor drift, not a correctness bug).

⚠️ 🟢 **No integration test** that counters update correctly through the full payment-approval
path — only the scoring formula is unit-tested.

---

## Module 10 — Reporting & Analytics

✅ All six reports implemented, sharing one date-range resolver and one filter UI.

✅ **Date-range input validation added** — reversed ranges swapped, ranges over one year clamped.

❌ No CSV/PDF export (PRD stretch goal — intentionally deferred, not forgotten).

❌ **No pagination on any report table** — fine at current scale; will degrade once a restaurant
has months of order history. (The Audit Log viewer in Module 8 *does* paginate — apply that
pattern here when report tables grow.)

⚠️ 🟡 **Reports load full result sets into memory** — no server-side paging or streaming.

---

## Cross-Cutting Gaps (not owned by any one module)

❌ 🔴 **Rotate credentials if previously committed** — see Module 0.

❌ 🟠 **No integration or controller tests** — three test files cover pure business logic only.
Nothing tests a controller action, an EF Core query, a SignalR broadcast, the Razorpay flow, or
password-reset token logic end-to-end.

❌ 🟠 **No CI/CD pipeline** — no automated build/test/deploy on push.

❌ 🟠 **No deployment configuration** — proposal names IIS/Azure App Service; nothing here
(env-specific config, publish profiles, secrets management) prepares for either.

❌ 🟡 **Security surface not penetration-tested** — open SignalR hub, session cart cookies,
password-reset timing side-channel. Deliberate trade-offs exist but none have been adversarially
tested.

✅ **Brand asset present** — `wwwroot/images/logo.png`.

❌ 🟡 **External CDN dependencies** — Google Fonts, Chart.js, SignalR JS, Razorpay checkout.js
all require network access at runtime.

✅ **`/health` endpoint** added for load-balancer probes.

⚠️ 🟢 **No structured application logging** beyond default ASP.NET Core log levels.

⚠️ 🟢 **Cookie auth uses `SecurePolicy.Always`** — correct for HTTPS dev; HTTP-only access
would fail to set auth cookies.

---

## Suggested Next Order

1. ~~**Module 2 gaps** (password reset, admin unlock)~~ — **done.**
2. ~~**Module 5 gaps** (true webhook, refunds)~~ — **done.**
3. ~~**Module 4 gaps** (live cart total, menu images, quantity cap)~~ — **done.**
4. ~~**Module 8 gaps** (duplicate-table validation, staff edit, audit log viewer)~~ — **done.**
5. ~~**Module 10 gap** (date-range input validation)~~ — **done.**
6. ~~**Module 0** (build + initial migration)~~ — **done** (Aug 2026 audit).
7. ~~**Rotate and remove committed secrets**~~ — **done** (placeholders in repo; use
   `appsettings.Development.json` for local secrets; rotate if credentials were ever in git).
8. ~~**Fix config mismatches**~~ — **done** (BaseUrl, LocalDB default, example Development file).
9. ~~**Add missing assets and routes**~~ — **done** (logo, Home/Error, `/health`).
10. 🟠 **Run first full smoke test** — `dotnet ef database update`, then login → QR scan →
    order → pay (Razorpay test keys) → kitchen → serve.
11. ~~**Harden open endpoints (partial)**~~ — **done:** table-scoped status/checkout, CSRF on
    `/payment/failed`, kitchen status validation, last-admin guard. **Still open:** SignalR auth/groups.
12. ~~**Update stale docs and views**~~ — **done** (`GETTING_STARTED.md`, `ForgotPassword.cshtml`,
    seed comment, this file).
13. **Remaining known gaps, lowest risk to defer:** SignalR auth/groups (Module 7), no image
    *upload* pipeline (Module 8), ASP.NET Core Identity vs. custom auth (Module 2), KDS
    load-testing (Module 7), report pagination/export (Module 10), CI/CD and deployment config
    (Cross-Cutting), Razorpay end-to-end verification (Module 5).
