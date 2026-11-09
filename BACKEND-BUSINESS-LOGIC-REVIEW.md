# Backend Business-Rule / Logic Review (ProductAuction.API)

Date: 2026-11-09 (repo state at review). Scope: `ProductAuction.API/Controllers`, `Services/Background`, relevant models/DTOs. Evidence cited by file:line.

## High

### 1) Seller can bid on / buy out own auction (self-dealing) — **FIXED (2026-11-09)**
- `Controllers/BidController.cs`: `PlaceBid` and `Buyout` now return `403` when `product.SellerId == userId` (“Sellers cannot bid on or buy their own auction.”).
- (Original) no ownership guard allowed a buyer-role user who owned the product to self-deal.

### 2) Admin password-reset endpoint vs staff intent — **FIXED (2026-11-09)**
- `Controllers/AdminController.cs:321-323` now uses `[Authorize(Policy="StaffPasswordReset")]` (overrides controller role gate for this action). `Program.cs` defines `StaffPasswordReset`: Admin/Super Admin, or any authenticated user with a role that is not `Buyer`/`Seller` (custom staff).
- Target guard kept: `platform-system-account` blocked; targets with `Buyer`/`Seller` roles blocked (must use `POST /api/auth/change-password` with old password).
- `AuthController.cs:366-370` remains the general self-service endpoint requiring current password.

## Medium

### 3) Frontend allows custom staff to admin UI; backend denies (authorization mismatch)
- Frontend: `bidora-client/src/components/Auth/RoleGuard.jsx` (`allowStaff` = `Admin` or any role not `Buyer`/`Seller`), `App.jsx` admin routes use `allowStaff`, `AuthenticatedLayout` treats custom roles as staff.
- Backend admin surfaces require `Admin,Super Admin` only:
  - `Controllers/AdminController.cs:16`
  - `Controllers/RoleController.cs:11` (also hides `Super Admin` from role list for non–Super-Admins — matches priority rule)
  - `Controllers/CategoryController.cs:63,107`
  - `Controllers/DisputeController.cs:37,337`
  - `Controllers/ProductController.cs:755,819,878`
  - `Controllers/ProfileController.cs:86,133`
  - `Controllers/SaleController.cs:37`
  - `Controllers/AuctionController.cs:477` (configure) and `:538` treats `Admin|Super Admin` as admin for auction mgmt
  - `Controllers/BidController.cs:264`
- **Impact:** custom staff users reach admin pages but API calls 403; confusing UX and broken features.
- **Fix:** define a single “staff” policy (e.g., roles in configured set or claim-based) and apply consistently server-side; align frontend to the same capability (don’t infer staff as “not Buyer/Seller” unless backend matches). Preserve Super-Admin > Admin restrictions already added (edit/create Admin/Super Admin, role visibility).

### 4) `DispatchOverdueBackgroundService` only marks status (no financial/state follow-through)
- `Services/Background/DispatchOverdueBackgroundService.cs:33-39`: sets `DeliveryStatus.DispatchOverdue` and saves; no escrow release/refund, no penalty, no product/sale state transition.
- Contrast: `AuctionResultBackgroundService` releases reservations/creates sale-related state for no-winner/sold paths; `PaymentOverdueBackgroundService` marks `SaleStatus.PaymentOverdue` (product/deposit release intentionally rolled back per decision).
- **Impact:** overdue dispatch may leave sale/delivery in a limbo unless disputes/admin flows resolve it (and `DisputeController` allows disputes in relevant states — verify). Can be correct if resolution is manual via dispute, but it’s a business-flow gap if funds/product should auto-settle.
- **Fix:** confirm intended flow; if auto-resolution needed, add idempotent handling + transaction + wallet/escrow updates mirroring dispute outcomes.

## Low / Risks

### 5) Concurrency/race risk on balance mutations
- Examples: `AdminController.ReviewDepositRequest` (`:849+`) approves and credits wallet without explicit DB transaction/rowversion; `SaleController.PayForSale` uses a transaction but wallet/reservation updates should be checked for concurrent reviews/payments/double-submit.
- **Impact:** duplicate credit/debit or inconsistent `Balance/ReservedBalance` under concurrent requests.
- **Fix:** wrap approve+credit in a transaction; re-check status inside transaction; consider optimistic concurrency (`[Timestamp]`/rowversion) on `Wallet`/`DepositRequest` or unique constraints/idempotency where applicable.

### 6) Role creation vs protected names (minor)
- `Controllers/RoleController.cs:96+` `CreateRole` checks `RoleExistsAsync` but not `IsProtected`/reserved names; rename/delete are protected (`:143,187`). Identity normalization usually prevents case-variant duplicates, but creating a confusingly similar/custom privileged name is possible by design unless restricted.
- **Fix (optional):** block creating names that collide with protected/system semantics or normalize/validate against policy.

## Verified / OK (spot checks)
- `DisputeController` raise-dispute blocks `AwaitingPayment`/`Completed` and restricts to buyer/seller (`:240-330` context read).
- `SaleController.PayForSale` checks sale ownership window/status, deadline → `PaymentOverdue`, adjusts balance/reserved and moves funds to escrow within a transaction (`:350+`).
- `BidController` enforces auction active, not ended, product approved/available, buyout threshold, and increment; buyout checks existing sale/current highest (`:20-110,390-420`).
- Product approval/admin product endpoints require `Admin,Super Admin` (`ProductController:755,819,878`).
- Role `Super Admin` treated as highest priority in edit/create/role-list visibility as recently implemented; `RoleSeeder`/`AdminAccountSeeder` seed it; protected roles guarded.

## Suggested Priority
1. Block seller self-bid/self-buyout (#1).
2. Align reset-password authorization with intended staff scope (#2) and resolve global custom-staff policy (#3).
3. Decide/implement dispatch-overdue financial outcome (#4).
4. Add transaction/idempotency guards to deposit review and recheck wallet races (#5).
