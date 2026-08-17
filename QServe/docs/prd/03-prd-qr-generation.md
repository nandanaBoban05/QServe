# PRD — Module 3: QR Code Generation

**Depends on:** Module 1 (Database & Core Domain)
**Consumed by:** Customer Ordering (the entry point), Admin Dashboard (table management)
**Estimated effort:** 0.5 weeks

## 1. Objective
Generate a unique, table-specific QR code that opens the customer ordering interface directly to that table's session — no app install, no manual table-number entry.

## 2. Scope
### In Scope
- QR code image generation using the QRCoder library (1.4+)
- Encoding a signed URL per table (see security note below) into the QR
- Admin-facing action to (re)generate and download/print a QR code per `RestaurantTable`
- Storing the encoded URL in `RestaurantTables.QRCodeData`

### Out of Scope
- Physical printing/label design
- Dynamic per-session QR rotation (out of scope for v1 — one static QR per table is sufficient)

## 3. Functional Requirements
| ID | Requirement |
|---|---|
| QR-1 | Each `RestaurantTable` has exactly one QR code encoding a URL like `/order/table/{TableID}?token={signedToken}` |
| QR-2 | Scanning the QR opens the customer ordering interface (Module 4) directly scoped to that table |
| QR-3 | Admin can regenerate a table's QR code (e.g., if a table is renumbered or a token is suspected compromised) |
| QR-4 | Admin can view/download a printable QR image for any active table |
| QR-5 | QR codes for inactive tables (`IsActive = 0`) should not resolve to a working order flow |

## 4. Process Logic
```
GenerateQR(tableId):
  table = get RestaurantTable by tableId
  token = sign(tableId + secret)  // HMAC or similar, prevents guessing table IDs
  url = baseUrl + "/order/table/" + tableId + "?token=" + token
  qrImageBytes = QRCoder.Generate(url)
  table.QRCodeData = url
  save
  return qrImageBytes (PNG)
```

## 5. Security Requirements
- The token in the URL must be signed (HMAC-SHA256 with a server secret) so table IDs can't simply be incremented by a malicious customer to browse other tables' sessions.
- Validate the token server-side on every request into the customer ordering flow (Module 4) — reject and show a friendly error if invalid or table inactive.
- QR generation and regeneration actions require Admin/Manager role (depends on Module 2).

## 6. Acceptance Criteria
- [ ] Scanning a generated QR with a real smartphone camera opens the ordering page for the correct table, no app prompt
- [ ] Tampering with the `token` query parameter (e.g., changing table ID without a valid token) is rejected
- [ ] Deactivating a table breaks its QR's ordering flow with a clear "table not available" message
- [ ] Admin can download a QR image suitable for printing (PNG, reasonable resolution e.g. 500x500px)

## 7. Interfaces Exposed to Other Modules
- `IQrCodeService.GenerateForTable(tableId) → byte[] pngImage`
- `IQrCodeService.ValidateToken(tableId, token) → bool` — Module 4 calls this before rendering the menu
