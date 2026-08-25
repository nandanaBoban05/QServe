# Getting Started

This is **QServe** — Smart Restaurant Management — a scaffolded starting point matching
Module 1's PRD (`docs/prd/01-prd-database-core.md`) exactly for its data model: 8 entities, all
constraints, and seed data. The project was renamed from an earlier internal working name
("RestaurantOrdering") to QServe across every namespace, project file, and folder — see the
rename notes near the bottom of this file if you're diffing against an older download.

It builds and tests cleanly on .NET 8 (`dotnet build`, `dotnet test` — 15 tests pass). The
initial EF migration is already in `QServe/Migrations/`. Copy
`appsettings.Development.json.example` to `appsettings.Development.json` (gitignored) for local
secrets and connection overrides.

## Branding
The logo at `wwwroot/images/logo.png` is the real QServe mark (not a placeholder) — it's used
as the favicon and appears in the sidebar, the Kitchen topbar, every auth screen (Login, Forgot
Password, Reset Password, Access Denied), the customer-facing ticket pages, and the Razorpay
checkout screen. It has its own cream/paper background baked in, so on dark surfaces (the
sidebar, the Kitchen topbar) it's wrapped in a small rounded badge (`.brand-logo` in
`theme.css`) rather than floating as a bare rectangle — use `.brand-logo--plain` on light
surfaces (auth cards, customer pages) to skip that badge. If you swap in a refined/transparent
version of the logo later, both classes still work; you'd just stop needing the badge treatment.

## UI Design — "Chit & Table" theme
Every page in the project shares one visual system, defined in `wwwroot/css/theme.css` and
loaded globally via `Views/Shared/_Layout.cshtml`. The design concept: this software digitises
the paper kitchen order chit, so that's the one subject-specific artifact everything is built
from — perforated ticket-edge cards, a rubber ink-stamp badge, a "punch tracker" for order
status, and monospace numerals set like a receipt printer, instead of a generic dashboard look.
"Chit & Table" was the working design-system name during development; the product itself is
QServe — the theme file's internal comments still refer to the design system by that name,
which is fine, they're describing the visual language, not the product.

**Two registers, one brand:**
- **Customer pages** (Menu, Cart, Status, TableOrders, Payment checkout) get the full
  ticket-stub expression — perforated cards, the stamp badge, the punch tracker, plus a small
  logo+wordmark strip at the top of each page — since this is what a diner actually sees on
  their own phone.
- **Staff pages** (Admin, Kitchen, Login) now use a persistent sidebar (Admin) or topbar
  (Kitchen) with the logo, dense data tables, stat cards with a small torn-corner accent, and
  the stamp motif used sparingly — carrying the same palette and type so it reads as one
  product rather than two different apps bolted together.

Palette, type, and every component class (`.ticket-card`, `.punch-tracker`, `.data-table`,
`.stat-card`, `.sidebar`, `.btn-primary`, etc.) are documented with comments at the top of
`theme.css` — read that file before adding a new page, and reuse its classes rather than
writing new inline styles. If you add a new view, give it `Layout = "_Layout"` (or rely on
`_ViewStart.cshtml`'s default) and it inherits the fonts and theme automatically.

## Prerequisites
- .NET 8 SDK
- SQL Server (LocalDB is fine for dev) — or swap the provider for PostgreSQL, see note below
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`

## First run
```bash
dotnet restore
copy QServe\appsettings.Development.json.example QServe\appsettings.Development.json
dotnet ef database update --project QServe
dotnet run --project QServe
```
The migration already exists (`Migrations/20260817025447_initial.cs`) — only run
`dotnet ef migrations add ...` if you change the entity model. Edit
`appsettings.Development.json` for your local SQL Server instance, Razorpay test keys, and
Gmail app password. Committed `appsettings.json` contains placeholders only.

## Running the tests (Module 6, Module 9)
```bash
dotnet test
```
`QServe.Tests` covers the AI Order Prioritisation classifier, the priority queue
sort, and the Recommendation Engine's scoring formula — all pure functions with no
DB/HTTP dependency, so these run instantly with no setup. This is the pattern to follow for
future modules' business logic: keep the core rule free of EF Core/HTTP calls, and it stays
this cheap to test.

## What's here
```
QServe-Solution/
  QServe.sln          References both projects below
  GETTING_STARTED.md              This file
  QServe/              Main web app
    Models/              8 entity classes matching the Module 1 schema field-for-field,
                          plus AccessFailedCount/LockoutEnd added on User for Module 2's lockout policy
    Data/
      ApplicationDbContext.cs   DbContext with all FK/unique/CHECK constraints + seed data
    Services/
      IAuthService.cs / AuthService.cs             Login validation + lockout logic (Module 2)
      IPasswordResetService.cs / PasswordResetService.cs   Self-service Forgot/Reset Password
      IEmailSender.cs / DevEmailSender.cs / GmailEmailSender.cs   DevEmailSender when
                                                      EmailConfig has placeholders; Gmail SMTP
                                                      when real credentials are configured
      IQrCodeService.cs / QrCodeService.cs          Signed per-table QR generation + validation (Module 3)
      IPaymentService.cs / PaymentService.cs        Razorpay checkout + admin verification (Module 5)
      IOrderClassifier.cs / OrderClassifier.cs      Quick/Regular/Heavy classification (Module 6)
      IPriorityQueueBuilder.cs / PriorityQueueBuilder.cs   Kitchen queue sort (Module 6)
      IRealtimeNotifier.cs / RealtimeNotifier.cs    Thin seam over KitchenHub (Module 7)
      IRecommendationService.cs / RecommendationService.cs   Popularity scoring (Module 9)
      RecentOrderedRecalculationService.cs          BackgroundService — nightly recompute (Module 9)
      IReportingService.cs / ReportingService.cs    Six report queries (Module 10)
    Hubs/
      KitchenHub.cs           The single SignalR hub shared by every module that needs live updates
      RealtimeDtos.cs         OrderStatusUpdateDto, PaymentPendingDto — broadcast payload shapes
    Controllers/
      AccountController.cs   Login/Logout, ForgotPassword/ResetPassword (self-service), redirects by role
      AdminController.cs     [Authorize(Roles="Admin,Manager")]; dashboard (now with a 7-day
                              trend chart + recent-activity feed), order monitor, table
                              management, QR actions, payment verification queue,
                              Payments/RefundPayment (refunds), AuditLog viewer
      AdminMenuController.cs [Authorize(Roles="Admin,Manager")]; menu items + categories (Module 8)
      AdminStaffController.cs [Authorize(Roles="Admin")] ONLY — staff CRUD, ClearLockout,
                              ResetPassword (admin-assisted); writes to AuditLogs for every action (Module 8)
      AdminReportsController.cs [Authorize(Roles="Admin,Manager")]; all six Module 10 reports
      KitchenController.cs   Module 7 — [Authorize(Roles="Kitchen")], priority-sorted queue,
                              Preparing/Ready/Served actions, broadcasts on every change
      CustomerController.cs  Module 4 — menu, session cart, checkout, live status via SignalR. [AllowAnonymous].
      PaymentController.cs   Module 5 — Razorpay checkout launch, signed confirm callback,
                              true server-to-server webhook (RazorpayWebhook). [AllowAnonymous].
    ViewModels/
      AdminDashboardStats.cs   Dashboard shape — stats, 7-day trend, recent activity (Module 8)
      ReportDtos.cs            One DTO per report — DailySalesSummaryDto, PopularItemDto, etc. (Module 10)
    Views/Account/        Login.cshtml, ForgotPassword.cshtml, ResetPassword.cshtml, AccessDenied.cshtml
    Views/Admin/           Index.cshtml (dashboard w/ Chart.js trend + activity feed),
                            Orders.cshtml (live monitor), Tables.cshtml, PaymentQueue.cshtml,
                            Payments.cshtml (refunds), AuditLog.cshtml — live via SignalR where relevant
    Views/AdminMenu/       Index.cshtml, CreateItem.cshtml, EditItem.cshtml, Categories.cshtml
    Views/AdminStaff/      Index.cshtml, Create.cshtml, Edit.cshtml, ResetPassword.cshtml
    Views/AdminReports/    Sales, PopularItems, Throughput, PaymentLog, OrderStatus, TableUtilisation
    Views/Customer/        Menu.cshtml (shows "Popular" badges — Module 9), Cart.cshtml,
                            Status.cshtml (live via SignalR), TableOrders.cshtml (re-order
                            history for this session), InvalidTable.cshtml — all now carry a
                            small logo+wordmark strip at the top
    Views/Payment/          Checkout.cshtml — Razorpay widget launch page
    Views/Kitchen/          Index.cshtml — the KDS screen, live via SignalR
    Views/Shared/_AdminNav.cshtml   The sidebar — shared across all Admin*/AdminMenu/AdminStaff/AdminReports views
    Views/Shared/_ReportsNav.cshtml / _DateRangeFilter.cshtml   Shared across the six report views
    wwwroot/css/theme.css   The whole visual system — read the header comment before adding a page
    wwwroot/images/logo.png   The real QServe logo — used as favicon + everywhere the brand appears
    Views/_ViewImports.cshtml / _ViewStart.cshtml   Tag helpers + default layout
    Program.cs            MVC + cookie auth + session + HttpClient + SignalR wired up
    appsettings.json      Connection string, App:BaseUrl, QrCode:SigningSecret, Razorpay:* placeholders
    docs/prd/             All 11 PRD files, so the specs live in the repo
  QServe.Tests/        xUnit tests for Modules 6 and 9's pure business logic
```

## Switching to PostgreSQL
Replace the `Microsoft.EntityFrameworkCore.SqlServer` package reference in the `.csproj` with
`Npgsql.EntityFrameworkCore.PostgreSQL`, and change `UseSqlServer(...)` to `UseNpgsql(...)` in
`Program.cs`. The check constraint syntax used in `ApplicationDbContext.cs` is standard SQL and
works on both providers as written.

## Seeded login (Module 2)
The seeded admin account now has a real BCrypt hash:
- **Email:** `admin@restaurant.local`
- **Password:** `ChangeMe123!`

Change this password (or the seed data entirely) before this goes anywhere near production —
it's here purely so you have something to log in with on day one.

## Auth design notes
- This uses **cookie authentication backed directly on the `Users` table**, not full ASP.NET
  Core Identity — the PRD left that choice open, and Identity's default schema doesn't match
  this project's bespoke `Users` table without extra mapping work. `IAuthService` is the seam
  to replace if you want full Identity later.
- Lockout: 5 failed attempts locks the account for 15 minutes (`AuthService.cs` constants at
  the top — tune as needed). This uses two columns (`AccessFailedCount`, `LockoutEnd`) added
  to `User` beyond the original Module 1 schema — regenerate your migration to pick these up.
- **Admin-assisted recovery is built** (was a flagged gap, now closed): `AdminStaffController`
  has `ClearLockout` (immediately clears a lockout rather than waiting 15 minutes) and
  `ResetPassword` (sets a new temporary password, shared with the staff member out-of-band —
  this is admin-assisted, not a self-service email flow, which stays explicitly out of scope).
  Both, plus `Create`/`Edit`/`ToggleActive`, now write to `AuditLogs` — that controller
  silently didn't before.
- `KitchenController` is real (Module 7); `AdminController`'s `Index` is a real dashboard
  (Module 8) — both replaced their original placeholder `Content(...)` responses.

## Where things actually stand
Every module from `docs/prd/00-README.md` has a working implementation. `GAP-ANALYSIS.md` at
the solution root is the honest, current audit of what's simplified, deviated, or still
missing per module — read that alongside this file rather than assuming "10 modules done"
means "nothing left." It's kept up to date as gaps get closed, not just written once.

Before running, regenerate your EF Core migration to pick up the new `User` columns — two
from Module 2's lockout policy, plus two more added for this pass's Forgot Password feature:
```bash
dotnet ef migrations add AddAuthAndPasswordResetFields --project QServe
dotnet ef database update --project QServe
```
(If you already generated `AddAuthLockoutFields` against an earlier download of this project,
just add one more migration on top for `PasswordResetTokenHash`/`PasswordResetTokenExpiry`
instead — EF Core doesn't need you to start over.)

## Forgot Password / rename notes (this pass)
- **Renamed from an internal working name to QServe** across every namespace, `.csproj`,
  `.sln`, and folder — done via `sed` across all 71 source files, not a partial find-and-replace.
  The `docs/prd/` files were deliberately **not** rewritten — they're the original academic
  project proposal/specs and stay as historical reference documents; only the running
  application (code, UI, page titles) is branded QServe.
- **Forgot Password is a real, working self-service flow**, not a stub: `AccountController`
  gained `ForgotPassword`/`ResetPassword` actions, backed by `IPasswordResetService`. A reset
  token is a 32-byte random value, only its SHA-256 hash is stored on `User`
  (`PasswordResetTokenHash`), and it expires after 1 hour and is single-use (cleared the moment
  it's redeemed) — same pattern as `QrCodeService`'s signed tokens and `PaymentService`'s
  webhook verification elsewhere in this codebase.
- **No real email sender is configured** — `DevEmailSender` just logs the email via `ILogger`
  instead of sending it, which is what makes this feature testable without SMTP/SendGrid
  credentials. `PasswordResetService` additionally surfaces the raw reset link directly on the
  Forgot Password confirmation page, but **only when `IWebHostEnvironment.IsDevelopment()` is
  true** — in Production it always returns `null` regardless of whether the email matched an
  account, so the enumeration-safety property (never reveal whether an email exists) holds
  there. Swap `DevEmailSender`'s DI registration in `Program.cs` for a real `IEmailSender`
  before this goes anywhere near production; nothing else needs to change.
- **Admin-assisted reset (`AdminStaffController.ResetPassword`) is unchanged and still exists
  separately** — that's for when an Admin sets a temporary password directly (e.g., a new hire,
  or someone who can't access their email). The two flows are independent; a self-service reset
  doesn't require any admin action, and vice versa.
- **The QServe logo (`wwwroot/images/logo.png`) is the actual provided asset**, not a
  placeholder — it appears as the favicon and across every staff and customer surface. Since it
  has its own cream background, `.brand-logo` wraps it in a small rounded badge on dark
  surfaces (sidebar, Kitchen topbar) so it reads as an intentional mark rather than a stray
  rectangle; `.brand-logo--plain` skips that badge on already-light surfaces (auth cards,
  customer pages). If you get a transparent-background version of the logo later, both classes
  keep working — you'd just stop needing the badge on dark surfaces.
- **The Admin sidebar (`_AdminNav.cshtml`) replaced the earlier horizontal topbar** for the
  Admin section specifically — Kitchen kept its topbar (a KDS screen benefits from vertical
  space more than a persistent side nav). The CSS uses a sibling selector
  (`.sidebar ~ .page { margin-left: 240px; }`) so every existing admin view got pushed clear of
  the fixed sidebar automatically, without editing each view's markup individually — verified
  across all 20 admin views that render `_AdminNav`.
- **The Admin dashboard is a real dashboard now**, not three numbers: it adds active
  order/table/menu-item/staff counts, a Chart.js 7-day revenue+orders trend (loaded from
  cdnjs, dual-axis bar+line), a recent-activity feed pulled live from `AuditLogs` (the same
  data the `/Admin/AuditLog` viewer shows, just the newest 8 entries here), and a quick-actions
  grid linking to the screens an admin actually opens most.

## Reporting & Analytics notes (Module 10)
- **Split into its own controller** (`AdminReportsController`), same reasoning as
  `AdminMenuController`/`AdminStaffController` in Module 8 — six reports is enough surface
  area to deserve separation rather than growing `AdminController` further.
- **One shared date-range resolver** (`ResolveRange` in the controller) rather than six
  separate implementations — every report treats "no dates given" the same way (a sensible
  default window per report: daily for Sales/Throughput/PaymentLog/OrderStatus, weekly for
  PopularItems/TableUtilisation) and every report accepts the same `?start=&end=` query
  string shape via the shared `_DateRangeFilter.cshtml` partial.
- **A real bug got fixed while building this module, not just documented as a gap**: rejected
  Cash/Card payments previously never got a `VerifiedBy`/`VerificationTime` stamp in
  `PaymentService.AdminVerifyAsync` — only approvals did. That meant every rejection an admin
  ever made would have silently been invisible to this report. Rejections now stamp
  `PaymentStatus = Failed` (there's no dedicated "Rejected" value in the Module 1 schema's
  four-state CHECK constraint, so `Failed` is the accurate fit) plus `VerifiedBy` and
  `VerificationTime`, same as approvals. If you're diffing this scaffold against an earlier
  download, that's the one behavioural change outside of Module 10 itself.
- **Popular Items (Module 10) and the "Popular" badge (Module 9) intentionally answer
  different questions** and can legitimately disagree: Module 9 scores every item by a
  weighted blend of all-time + trailing-7-day volume for the customer-facing badge; this
  report ranks items by actual sales strictly within whatever date range you pick. An item
  can be "Popular" on the menu badge while ranking low in a Popular Items report scoped to
  yesterday only — that's not a bug in either one.
- **Kitchen Throughput's per-type attribution is a judgment call, not the only valid reading
  of REP-4.** The PRD defines prep time at the order level (`ServedAt - ApprovedAt`) but an
  order can contain multiple item types; this implementation credits each distinct type
  present in an order with that order's *full* duration (not a split fraction), since kitchen
  prep happens in parallel rather than sequentially per item. Documented in the view itself,
  not just here, since it materially affects how to read the numbers.
- **Revenue-bearing reports exclude `Cancelled` orders**; the Order Status Report deliberately
  does not, since showing the cancellation rate is its entire purpose (REP-6). This matches
  the convention already established by the Module 8 dashboard's "Revenue Today" figure — if
  any two of these ever disagree on the same range, that's a real bug worth chasing down.

## Recommendation Engine notes (Module 9)
- **`ComputeScore` is a plain function, same pattern as Module 6's classifier** — no DB access,
  directly unit-tested. `GetPopularItemsAsync` does the DB fetch, then scores and sorts in
  memory rather than translating the formula into SQL — the menu table is small enough that
  this is not a real performance concern, and it keeps the scoring logic in one testable place
  instead of duplicated as a LINQ-to-SQL expression.
- **`TotalOrdered` increments on approval, alongside the Module 6 classifier call** — both live
  in the same two spots in `PaymentService` (`ConfirmOnlinePaymentAsync` and the approve branch
  of `AdminVerifyAsync`). If you ever add a third way for an order to become `Approved`, both
  the classifier *and* the recommendation hook need to go there — they're deliberately
  side-by-side in the code as a reminder.
- **`RecentOrdered` is *recomputed* from scratch every run, not incremented live.** A running
  counter that's incremented on new orders and never decremented would only ever grow — you'd
  need a separate decay mechanism anyway, and a derived value (SUM of the last 7 days, fresh
  each time) can't drift from reality the way a stateful counter eventually does. Don't
  "optimize" this into a live increment without solving the decay problem properly first.
- **`RecentOrderedRecalculationService`** is a `BackgroundService` that runs the recompute once
  on startup and then every 24 hours — a real (if simple) nightly job, not a TODO. It creates a
  fresh DI scope per run since `ApplicationDbContext` is scoped but the background service
  itself is a singleton; don't inject `ApplicationDbContext` directly into a `BackgroundService`
  constructor, it'll throw at startup.
- **Popular badges only ever appear on available items** — `GetPopularItemsAsync` filters on
  `IsAvailable` before scoring, so toggling an item off in Module 8's menu management also
  removes its badge, with no separate code path to keep in sync.
## Admin Dashboard notes (Module 8)
- **Split into three controllers, not one.** `AdminController` (dashboard/orders/tables/payment
  queue), `AdminMenuController` (menu items/categories), `AdminStaffController` (staff
  accounts). This wasn't arbitrary — `AdminStaffController` carries a class-level
  `[Authorize(Roles = "Admin")]` with no Manager access at all (ADM-9), and keeping it a
  separate controller means that restriction is the whole story: nobody has to trace through
  individual action bodies to confirm Manager can't reach staff management. If you add new
  admin functionality, ask which of the three it belongs with (or whether it's a fourth) rather
  than defaulting to the biggest existing controller.
- **`_AdminNav.cshtml`** is a shared partial included at the top of every Admin/AdminMenu/
  AdminStaff view — it conditionally shows the Staff link only via `User.IsInRole("Admin")`.
  That's a UI convenience, not a security boundary — `AdminStaffController`'s `[Authorize]`
  is what actually stops a Manager from reaching it, per AUTH-6's "role checks enforced
  server-side, not just hidden UI links." Don't let the nav's conditional visibility become
  the only thing guarding a future action.
- **Dashboard revenue figures exclude `Cancelled` orders**, matching the convention Module 10's
  Reporting PRD specifies — so if you build Module 10 next, its Daily Sales Summary numbers
  should agree with this dashboard's "Revenue Today" figure. If they ever disagree, one of them
  has a bug.
- **`ToggleAvailability` and `ToggleCategoryActive`** are the two levers Module 4's customer
  menu actually respects: an unavailable item is blocked from ordering (`AddToCart` checks
  `IsAvailable` server-side), and a hidden category disappears from the menu query entirely
  (`Menu` action filters on `Category.IsActive`). Toggling either takes effect on the
  customer's next page load — there's no cache to invalidate.
- **Staff creation reuses `AuthService`'s exact hashing** (`BCrypt.Net.BCrypt.HashPassword`) —
  if you ever change the hashing scheme in Module 2, update it here too, or newly-created
  accounts and Module 2's login will disagree about what a valid hash looks like.
- **`CreateTable` on a duplicate table number silently redirects** rather than showing an
  inline error — flagged as a known rough edge in the code, not hidden. Worth fixing with
  proper `ModelState`/`TempData` error display before this goes near a real deployment.

## QR design notes (Module 3)
- The token is a **deterministic HMAC-SHA256** of the table ID, signed with
  `QrCode:SigningSecret` from `appsettings.json`. Set a real random secret before this goes
  anywhere near production — the placeholder value is not safe to ship.
- Because it's deterministic, "Regenerate" in the Tables view only produces a *different* QR
  if the signing secret itself has changed — it's not a per-scan rotating code. That matches
  the PRD (dynamic per-session QR rotation is explicitly out of scope for v1). Rotating the
  secret invalidates every table's existing QR at once — use that if a token is ever suspected
  compromised.
- `QrCodeService.ValidateToken` also checks `RestaurantTable.IsActive` — an inactive table's
  QR stops working immediately even if someone kept the old printed code (QR-5). Module 4 must
  call this before rendering the menu; it should not trust the `tableId` in the URL alone.
- The generated PNG is ~500x500px (20px per QR module) — resize as needed for actual table
  tent cards or stickers.

## Customer Ordering notes (Module 4)
- **Cart lives in server-side session**, not a DB table — there's no customer identity to
  attach it to (no login, per PRD scope). The cart cookie is `SameSite=Lax` rather than
  `Strict` because customers arrive via a cross-site QR-scan navigation (their camera app),
  which `Strict` cookies would otherwise silently drop on the first request.
- **The Module 4 <-> Module 5 boundary is deliberate:** `CustomerController.Checkout` creates
  the `Order`, `OrderItems`, and a starting `Payment` row in the correct state per the
  lifecycle diagram (Online → `PendingPayment`, Cash/Card → `AwaitingVerification`) — and stops
  there. It does not call Razorpay and does not verify anything. Module 5 should treat any
  order sitting in those two states as its intake queue.
- **Order + OrderItems + Payment are all added via navigation properties in a single
  `SaveChangesAsync()` call**, so EF Core wraps the insert in one transaction — satisfying the
  "atomic DB transactions for order + payment operations" NFR without any manual transaction
  handling.
- **Status page uses genuine live push, not polling** — `Status.cshtml` subscribes to the
  `OrderStatusUpdate` SignalR broadcast from Module 7's `KitchenHub`, filtered by `orderID` so
  it only reacts to updates for its own order. A one-shot `fetch` fallback runs only if the
  SignalR handshake itself fails (see Module 7's notes below for why only this page gets that
  fallback and not the staff-facing screens).
- Menu availability (`IsAvailable`) is enforced twice: the menu view only ever renders
  available items (via a filtered `Include`), and `AddToCart` re-checks server-side in case
  something went stale between page load and the add request.
- **Re-ordering / placing a second round:** the schema never restricted a table to one order —
  `Orders.TableID` has no uniqueness constraint — so a second round is simply a second `Order`
  row, created the exact same way as the first, going through the same payment gate
  independently and appearing as its own kitchen ticket. Nothing about an already-placed order
  is reopened or modified.
  - `Menu`'s `token` parameter is now optional (`string? token = null`) and falls back to the
    session-stored copy set on first scan. This is what lets "Order More" / "Add More Items"
    links work as plain internal navigation without carrying the raw signed token around in
    every URL — still re-validated via `ValidateToken` either way, so it's not a new trust
    boundary.
  - `GetSessionOrderIds`/`AddSessionOrderId` track which `OrderID`s a table has placed during
    the current session (a `Session` list, not a DB query for "today's orders at this table")
    — deliberately session-scoped so a different party seated at the same table later doesn't
    see a stranger's past orders.
  - `TableOrders` (`/order/table/{tableId}/orders`) lists every order this session with a link
    to each one's live Status page — this is what the Menu banner ("N orders already placed")
    and the Status page's "View All Your Orders" link both point to.

## Payment Processing notes (Module 5)

**You need Razorpay test credentials to actually run this end to end.** Sign up at
razorpay.com, switch to Test Mode, and grab the Key ID / Key Secret from Settings → API Keys.
Put them in `appsettings.json` under `Razorpay:KeyId` / `Razorpay:KeySecret` (or better, user
secrets / environment variables — don't commit real keys). Without valid keys, `Checkout`
(Module 4's Online path) will fail when it tries to create a Razorpay order.

- **REST calls, not the official SDK.** `PaymentService` talks to Razorpay's Orders API
  directly over `HttpClient` with Basic Auth, rather than depending on the official Razorpay
  .NET SDK. This was a deliberate simplification to keep the dependency surface small and the
  logic auditable line-by-line — swap in the SDK later if you'd rather not hand-maintain the
  HTTP calls; `IPaymentService` is the seam, nothing else in the app needs to change.
- **Signature verification is the real security boundary**, not the "payment succeeded"
  callback itself. `ConfirmOnlinePaymentAsync` recomputes
  `HMAC-SHA256(key_secret, razorpay_order_id + "|" + razorpay_payment_id)` server-side and
  only approves the order if it matches what Razorpay's checkout.js handler returned. A forged
  call with an invalid signature is rejected and changes nothing (see the acceptance criteria
  in the Module 5 PRD) — this is deliberately what `/payment/confirm` is *not*
  anti-forgery-token-protected on: the signature check is the actual defense here, and wiring a
  CSRF token through a `fetch()` call would be redundant belt-and-suspenders. Flagged in code
  comments rather than silently omitted.
- **Two independent confirmation paths now exist, both funneling through one shared
  `ApproveOnlinePaymentAsync` helper:**
  - `/payment/confirm` — the browser-driven Checkout-JS success callback, fires the instant
    the customer's browser gets a success response (fast path, normal case).
  - `/payment/webhook/razorpay` — a genuine server-to-server webhook, the redundant safety net
    for when the browser callback never arrives (tab closed, network drop mid-checkout).
    Verifies `X-Razorpay-Signature` over the *raw request body* using a **separate**
    `Razorpay:WebhookSecret` — a different signature scheme from the checkout callback above,
    do not reuse one HMAC computation for the other. Register this exact URL in Razorpay's
    dashboard under Settings → Webhooks, subscribed to at least `payment.captured`.
  - Both paths check `PaymentStatus` before acting, so whichever arrives second is a no-op —
    an order can't be double-approved or double-classified by both firing.
- **Refunds** (`IPaymentService.RefundAsync`, exposed at `/Admin/Payments`) call Razorpay's
  refund API for Online payments; Cash/Card refunds just record the state change since that
  money moves physically at the till, not through the gateway. Only a `Received` payment can
  be refunded — the guard lives in `RefundAsync`, not just the UI.
- **Idempotency:** `ConfirmOnlinePaymentAsync`, the webhook handler, and `AdminVerifyAsync` all
  check the current `PaymentStatus` before acting, so a duplicate callback, a duplicate webhook
  delivery (Razorpay does not guarantee exactly-once), or a double-clicked Approve button can't
  double-process a payment or re-fire an audit log entry.
- **Admin verification queue** (`/Admin/PaymentQueue`) lists Cash/Card payments still `Pending`
  and lets an Admin/Manager approve or reject with one click, delegating straight to
  `IPaymentService.AdminVerifyAsync`. It updates live via the same `KitchenHub`
  `PaymentPendingVerification` broadcast Module 7 built — no polling.
- Every approval/rejection/refund writes an `AuditLogs` entry with the acting Admin's `UserID`
  (or `null` for the two automated paths, browser callback and webhook) — that's what feeds
  the Payment Verification Log report in Module 10.

## AI Order Prioritisation notes (Module 6)
- **This is deliberately not machine learning** — see the note at the top of
  `OrderClassifier.cs`. It's a handful of `if` statements over item counts. The proposal's
  "Artificial Intelligence / Expert Systems" category refers to this being a rule-based expert
  system, not a trained model — don't over-engineer it later unless the PRD's scope changes.
- **Hooked into both approval paths in `PaymentService`**: `ConfirmOnlinePaymentAsync` (Online)
  and `AdminVerifyAsync` (Cash/Card, approve branch only — a rejected order never reaches the
  kitchen, so it's never classified). If you add a third way for an order to become `Approved`
  later, remember to call `IOrderClassifier.ClassifyAndSaveAsync` there too.
- **`Classify()` takes a plain `IEnumerable<string>`, not DB entities** — that's what makes the
  test project able to construct test cases in one line with no database, no mocking
  framework, and no async ceremony. Keep this shape when you touch the module; the moment it
  needs a DbContext to run a test, you've lost the thing that made it easy to verify.
- `PriorityQueueBuilder` is equally pure — it sorts whatever list of `Order`s it's handed, and
  doesn't query anything itself. Module 7's Kitchen Display should fetch approved orders from
  the DB, then hand them to `Build()` — don't duplicate the sort logic inline there.

## Kitchen Display / real-time notes (Module 7)
- **One hub for the whole system**: `KitchenHub` at `/hubs/kitchen` is shared by the Kitchen
  Display, the Customer status page, and the Admin payment queue. Don't create a second hub for
  a new module later — add a new named event on this one (see `IRealtimeNotifier`) and have
  the relevant view subscribe to it, the same way `PaymentPendingVerification` was added
  alongside `OrderStatusUpdate` rather than getting its own connection.
- **`IRealtimeNotifier` is the seam** between business logic and SignalR specifics —
  `PaymentService`, `KitchenController`, and `CustomerController` all broadcast through it
  rather than injecting `IHubContext<KitchenHub>` directly. If you ever need to unit test
  `PaymentService`'s approval logic, mock `IRealtimeNotifier` rather than standing up a real
  hub.
- **The hub itself has no `[Authorize]`** — this is intentional, not an oversight. Anonymous
  customers need to receive `OrderStatusUpdate` on their status page, so the connection has to
  stay open to unauthenticated clients. Authorization still happens correctly at the MVC action
  level (`KitchenController` requires the `Kitchen` role, `AdminController` requires
  `Admin`/`Manager`) — the hub just carries messages, it doesn't gate who can trigger them.
- **Every KDS/status/queue page reloads or DOM-patches on a broadcast rather than trying to
  reconcile partial state.** `Kitchen/Index.cshtml` and `Admin/PaymentQueue.cshtml` do a full
  page reload on any relevant event; `Customer/Status.cshtml` patches just its own status text,
  filtered by `orderID`, since a full reload there would be a jarring customer-facing UX.
  That asymmetry is deliberate — pick whichever fits the screen, don't feel obligated to
  hand-patch the DOM everywhere.
- **`MarkServed` on `KitchenController` is a small scope extension** beyond the strict Module 7
  PRD (which only covers kitchen staff moving orders Approved → Preparing → Ready) — added so
  the demo loop actually closes without a tenth module. A real deployment might want that
  action on a separate front-of-house screen instead.
- **Degraded-connection fallback stayed cheap rather than duplicated everywhere**: only
  `Status.cshtml` (the one customer-facing page, on possibly-flaky restaurant Wi-Fi) has a
  one-shot `fetch` fallback if the SignalR handshake fails. The staff-facing screens (KDS,
  payment queue) assume a reasonable network and don't bother — they're used from
  restaurant-provided devices, not personal phones on cell data.
