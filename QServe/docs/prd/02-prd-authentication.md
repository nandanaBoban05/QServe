# PRD — Module 2: Authentication & RBAC

**Depends on:** Module 1 (Database & Core Domain)
**Consumed by:** Admin Dashboard, Kitchen Display (staff login), Payment Processing (VerifiedBy)
**Estimated effort:** 1 week

## 1. Objective
Provide secure login for staff (Admin, Kitchen, Manager roles) via ASP.NET Core Identity, with role-based access control gating every staff-facing controller. Customers never authenticate — their access is table-scoped via a signed URL (see Module 4).

## 2. Scope
### In Scope
- ASP.NET Core Identity integration against the `Users` table (or Identity's own tables, mapped/synced to `Users` — implementer's choice, document which)
- Login / logout pages (Razor Views)
- Role-based `[Authorize(Roles = "...")]` attributes protecting Admin and Kitchen controllers
- Session management: cookie-based, `HttpOnly`, `SameSite=Strict`
- Password hashing via BCrypt
- Basic account lockout after repeated failed attempts (brute-force mitigation)

### Out of Scope
- Self-service registration (staff accounts are created by an existing Admin — see Admin Dashboard PRD)
- Password reset via email (out of scope for v1 — Admin can reset directly)
- Customer accounts of any kind

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| AUTH-1 | Staff can log in with Email + Password |
| AUTH-2 | Invalid credentials show a generic error (do not reveal whether the email exists) |
| AUTH-3 | Successful login redirects based on Role: Admin/Manager → Admin Dashboard, Kitchen → KDS |
| AUTH-4 | Session persists via secure cookie; logout clears it |
| AUTH-5 | Any controller action outside the public customer flow requires authentication |
| AUTH-6 | Role checks are enforced server-side on every protected action, not just hidden in the UI |
| AUTH-7 | Inactive users (`IsActive = 0`) cannot log in even with correct credentials |
| AUTH-8 | Account locks for a configurable duration after 5 consecutive failed login attempts |

## 4. Data Model
Uses `Users` table as defined in Module 1. `Role` is one of `Admin`, `Kitchen`, `Manager`.

## 5. Process Logic
```
Login(email, password):
  find user by email where IsActive = 1
  if not found → generic "invalid credentials" error
  if locked out → "account temporarily locked" error
  verify password against BCrypt hash
  if mismatch → increment failed-attempt counter, generic error
  if match → reset failed-attempt counter, issue auth cookie, redirect by Role
```

## 6. Security Requirements
- Passwords hashed with BCrypt (work factor ≥ 11), never stored or logged in plaintext.
- Auth cookie: `HttpOnly`, `Secure`, `SameSite=Strict`.
- Anti-forgery token on the login POST.
- Rate-limit login attempts per IP in addition to per-account lockout.
- All failed and successful login events should be considered for `AuditLogs` (EntityType = "Users", Action = "LoginSuccess" / "LoginFailed").

## 7. Acceptance Criteria
- [ ] A seeded Admin user can log in and reach the Admin Dashboard route
- [ ] A Kitchen-role user cannot reach any Admin-only route (403, not just a hidden link)
- [ ] 5 failed logins lock the account; a 6th attempt is rejected even with the correct password
- [ ] Deactivating a user (`IsActive = 0`) immediately prevents new logins (existing sessions may be handled via short cookie expiry)
- [ ] No plaintext password ever appears in logs, DB, or network traffic (verify via HTTPS + hash inspection)

## 8. Interfaces Exposed to Other Modules
- `[Authorize(Roles = "Admin,Manager")]` — use on Admin Dashboard, Reporting controllers
- `[Authorize(Roles = "Kitchen")]` — use on KDS controllers
- `User.Identity` / `HttpContext.User` — other modules read the logged-in `UserID` for `VerifiedBy` (Payments) and `PerformedBy` (AuditLogs) fields
