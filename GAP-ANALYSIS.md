# Gap Analysis — What's Missing or Incomplete, Module by Module

This is an honest audit of the project against the 11 PRD files, done the same way the PRDs
were written — so it can be worked module-wise, same as the original build order. The project
itself was renamed from an internal working name to **QServe** partway through this log; that
rename doesn't get its own module entry below since it's not a PRD-tracked feature, but it's
noted in `GETTING_STARTED.md`. Status markers:

- ✅ **Done** — matches the PRD, no known gap
- ⚠️ **Partial** — implemented but simplified, deviated, or has a known rough edge
- ❌ **Missing** — not implemented at all

Read this alongside each module's "notes" section in `GETTING_STARTED.md` — this document is
the checklist; that one has the reasoning behind each call.

---

## 0. Foundational — blocks everything below

❌ **The project has never actually been built.** This sandbox has no NuGet registry access, so
`dotnet restore`/`dotnet build` has never run against this code. That means:
- No `Migrations/` folder exists yet — `dotnet ef migrations add InitialCreate` has never been
  executed, so there is no actual database schema file, only the `DbContext`/entity C# that
  *should* produce one.
- No guarantee the code compiles cleanly — 28+ Razor views and ~20 C# files were written by
  hand across many turns; a missed `using`, a typo'd property name, or a Razor syntax slip is
  entirely possible and has not been caught by a compiler.

**This is priority zero.** Before working any module below, run `dotnet restore`, fix whatever
build errors surface, then generate the initial migration. Everything else in this document
assumes that's done first.

---

## Module 1 — Database & Core Domain

✅ Schema matches the PRD's 8 tables field-for-field, with constraints, seed data.
⚠️ Two columns (`AccessFailedCount`, `LockoutEnd` on `User`) were added beyond the original
schema for Module 2's lockout — additive, documented, but means the schema and PRD have
diverged slightly; update `01-prd-database-core.md` if you want the doc to stay authoritative.
❌ No second migration has been generated for those two columns — see Module 2 below.

## Module 2 — Authentication & RBAC

✅ Login, role-based `[Authorize]`, BCrypt hashing, 5-attempt/15-minute lockout.
✅ **Admin unlock (`ClearLockout`) and admin-assisted password reset (`ResetPassword`) — built.**
An Admin can now clear a lockout immediately or set a new temporary password for any staff
account, both from the Staff list.
✅ **Self-service Forgot Password — built.** The Module 2 PRD explicitly deferred this
("out of scope for v1"), but it was later requested and is now real: `AccountController`'s
`ForgotPassword`/`ResetPassword` actions, backed by `IPasswordResetService` — hashed,
single-use, 1-hour-expiry tokens, the same defensive pattern (never reveal whether an email
exists) as `AuthService`'s login handling. No real email sender is wired up yet (see the
Cross-Cutting section) — `DevEmailSender` logs instead of sending, and the reset link is only
ever shown directly on-screen in the Development environment, never in Production.
✅ **Staff account edit (`Edit`) — built.** Name/email/role can now be changed in place instead
of only deactivate + recreate, which previously would have orphaned `AuditLogs.PerformedBy`
and `Payments.VerifiedBy` history pointing at the old account.
✅ **`AdminStaffController` now writes to `AuditLogs`** for every action (create, edit,
activate/deactivate, lockout clear, password reset) — it silently didn't before, which was an
unflagged gap until an earlier pass.
⚠️ **Used custom cookie auth instead of ASP.NET Core Identity** as the original proposal's tech
stack specifies. Functionally equivalent, but a real deviation from the stack list — worth a
conscious decision, not just inherited from how it happened to get built.
❌ Migration for `AccessFailedCount`/`LockoutEnd`/`PasswordResetTokenHash`/
`PasswordResetTokenExpiry` not yet generated (depends on Module 0).

## Module 3 — QR Code Generation

✅ HMAC-signed per-table token, PNG generation, admin download/regenerate, `IsActive` gating.
✅ Matches PRD scope — dynamic per-scan rotation was explicitly out of scope and stays that way.
⚠️ Never tested against a real phone camera — untested claim, not a known bug.

## Module 4 — Customer Ordering

✅ Menu browse, session cart, checkout handoff, live status, re-order flow (added on request).
✅ **Cart total now recalculates live, client-side.** `Cart.cshtml` recomputes each line total
and the grand total instantly on quantity input, via plain JS reading `data-price` attributes —
no server round-trip needed just to see the new total. The persisted change still requires
hitting "Update" (session write); this closes the *display* lag, which was the actual gap.
✅ **Menu item images now render.** `Menu.cshtml` shows `MenuItem.ImageUrl` as a thumbnail on
each ticket card when set, with `onerror` silently removing a broken/missing image rather than
showing a broken-image icon.
✅ **Server-side quantity cap added** (`MaxLineQuantity = 50` in `CustomerController`), applied
in both `AddToCart` and `UpdateCart`, plus a matching `max="50"` on the quantity inputs for
client-side UX. Not a business rule — just a sanity ceiling; raise it if genuine bulk orders
need more.

## Module 5 — Payment Processing

✅ Razorpay order creation, signature-verified checkout confirmation, admin verification queue,
idempotency guards, full audit trail (including the rejection-stamp fix made during Module 10).
✅ **True server-to-server webhook — built.** `POST /payment/webhook/razorpay` verifies
`X-Razorpay-Signature` over the raw request body using a *separate* `Razorpay:WebhookSecret`
(distinct scheme from the checkout signature) and is idempotent against the browser-driven
`ConfirmOnlinePaymentAsync` path already having approved the same payment. Both paths now
funnel through one shared `ApproveOnlinePaymentAsync` so the approval side-effects
(classification, popularity counters, broadcast) can't drift between them. Still needs to be
registered in Razorpay's dashboard (Settings → Webhooks) and given a real secret before it does
anything in practice.
✅ **Refunds — built.** `IPaymentService.RefundAsync` calls Razorpay's refund API for Online
payments (Cash/Card just records the state change, since that money moves physically at the
till). Exposed via a new `/Admin/Payments` screen listing all Received payments with a Refund
button — separate from the Orders monitor, which excludes Served orders and would've missed
post-serving refund requests.
⚠️ **REST calls instead of the official Razorpay .NET SDK** — documented deviation from the
proposal's tech stack, functionally fine but worth knowing it's not the "official" integration.
❌ Never tested against real Razorpay test-mode keys — the whole payment flow, webhook included,
is unverified in practice, not just in code review.

## Module 6 — AI Order Prioritisation

✅ Fully implemented and unit-tested against the PRD's exact acceptance criteria, including
boundary cases (2 vs. 3 heavy items) and the "same input → same output" purity check.
No known gaps.

## Module 7 — Kitchen Display System

✅ SignalR hub, priority-sorted queue, Preparing/Ready/Served actions, live broadcasts to
Kitchen/Status/Payment Queue screens.
⚠️ `MarkServed` was added beyond the PRD's strict scope (kitchen only owns
Approved→Preparing→Ready) to close the demo loop — a real deployment might want that action on
a separate front-of-house screen instead of the kitchen tablet.
❌ **The "100+ concurrent users, <2s response" NFR has never been load-tested.** SignalR with
`Clients.All` broadcasts to every connection regardless of relevance — fine at small scale,
but nobody has verified this holds up under real concurrent load.
❌ No multi-station routing (explicitly out of scope in the PRD — just noting it's still absent).

## Module 8 — Admin Dashboard

✅ Dashboard stats, live order monitor, table/menu/category CRUD, staff account management,
role-gated correctly (Admin-only staff controller).
✅ **`CreateTable` now shows a real inline error on a duplicate table number** (via `TempData`,
displayed on the Tables page) instead of silently no-oping — a success message shows too, so
the admin gets feedback either way.
✅ **Staff account edit — built.** `AdminStaffController.Edit` changes name/email/role in place,
so a mistake no longer requires deactivate-and-recreate (which would have orphaned
`AuditLogs.PerformedBy` / `Payments.VerifiedBy` history pointing at the old row).
✅ **Audit log viewer — built.** `/Admin/AuditLog` lists every `AuditLogs` entry, newest first,
paginated 50 at a time, showing who did what to which entity and when. The data existed since
Module 1 but was completely invisible until now.
✅ **Dashboard upgraded from three numbers to an actual management dashboard.** Added active
order/table/menu-item/staff counts, a 7-day revenue+order-count trend chart (Chart.js), a
recent-activity feed sourced live from `AuditLogs`, and a quick-actions grid.
✅ **Sidebar navigation replaces the earlier horizontal topbar** for the whole Admin section —
persistent, responsive (collapses to a horizontal strip under 860px), used consistently across
all 20 admin views via one shared partial (`_AdminNav.cshtml`) and one CSS sibling-selector
rule, so no view needed individual markup changes to get pushed clear of it.
❌ **No image upload pipeline** — `ImageUrl` is still a plain text field in the menu item forms;
there's no file upload, no storage integration (local disk, blob storage, etc.). PRD left this
as "implementer's choice" but no choice has been made yet — displaying a URL (Module 4's fix
above) is a different problem from *getting* a URL, which remains unsolved.

## Module 9 — Recommendation Engine

✅ Scoring formula, popularity counters wired into both payment-approval paths, nightly
recompute via a real `BackgroundService`, "Popular" badges on the customer menu.
No known functional gaps. Untested: whether the 24-hour `BackgroundService` interval survives
app restarts gracefully at scale (no persisted "last run" timestamp — a restart just starts a
fresh 24-hour countdown, which is a minor drift risk, not a correctness bug).

## Module 10 — Reporting & Analytics

✅ All six reports implemented, sharing one date-range resolver and one filter UI.
✅ **Date-range input validation added.** `ResolveRange` now swaps a reversed `start > end`
range back into the order the person almost certainly meant, and clamps anything over a year
wide rather than silently running a huge or empty query — a typo'd year no longer produces a
confusing blank report.
❌ No CSV/PDF export (the PRD explicitly marked this a stretch goal, not required — just noting
it's the one thing intentionally left undone rather than forgotten).
❌ No pagination on any report table — fine at current scale, will become a real problem once a
restaurant has months of order history. (The `AuditLog` viewer in Module 8 *does* paginate —
worth applying that same pattern here if report tables grow.)

---

## Cross-Cutting Gaps (not owned by any one module)

These don't map to a single PRD file but affect the whole project:

❌ **No real email sender configured.** `DevEmailSender` (new, backs Forgot Password) logs
instead of sending — genuinely fine for development, a real blocker for production. Swapping
in SMTP/SendGrid/etc. is a single DI registration change in `Program.cs` (`IEmailSender` is
the seam), but it hasn't been done, and no provider has been chosen.
❌ **No integration or controller tests** — the two test files (`OrderClassifierTests`,
`RecommendationServiceTests`) cover pure business logic only. Nothing tests a controller
action, an EF Core query, a SignalR broadcast, the Razorpay flow, or the new password-reset
token logic end-to-end.
❌ **No CI/CD pipeline** — not required by the proposal, but worth deciding whether to add one
before this goes further.
❌ **No deployment configuration** — the proposal names IIS/Azure App Service as hosting targets;
nothing here (env-specific config, deployment scripts, secrets management beyond
`appsettings.json` placeholders) prepares for either.
⚠️ **Security surface reviewed but not penetration-tested** — the deliberate anti-forgery
omission on `/payment/confirm` (documented, reasoned), the session-based cart's `SameSite=Lax`
cookie, the open (unauthenticated) SignalR hub, and the new password-reset token scheme are all
considered sound trade-offs or standard patterns, not oversights — but none of them have been
adversarially tested.
❌ **UI never rendered in a real browser** — the "Chit & Table" design system (including the
new sidebar, dashboard chart, and logo integration) was written blind, no browser available in
this sandbox. Spacing, contrast, the Chart.js dual-axis rendering, and responsive behavior at
real screen widths are all unverified.

---

## Suggested Next Order

1. ~~**Module 2 gaps** (password reset, admin unlock)~~ — **done.**
2. ~~**Module 5 gaps** (true webhook, refunds)~~ — **done.**
3. ~~**Module 4 gaps** (live cart total, menu images, quantity cap)~~ — **done.**
4. ~~**Module 8 gaps** (duplicate-table validation, staff edit, audit log viewer)~~ — **done.**
5. ~~**Module 10 gap** (date-range input validation)~~ — **done.**
6. **Module 0** (get it building + migrations generated) — the one thing left that this
   sandbox genuinely cannot do. Everything above has been written and reasoned through
   carefully, but **none of it has ever been compiled.** This is the real next step, not a
   formality — run `dotnet restore` and `dotnet build` first and expect to fix something.
7. **Remaining known gaps, lowest risk to defer:** no image *upload* pipeline (Module 8 — a
   URL field exists and now renders, but there's no way to get a URL other than typing one),
   ASP.NET Core Identity vs. the custom auth here (Module 2, a conscious-tradeoff item, not a
   bug), the KDS "100+ concurrent users" NFR being unverified (Module 7), and everything under
   Cross-Cutting Gaps above (tests, CI/CD, deployment config, a real browser render of the UI).
