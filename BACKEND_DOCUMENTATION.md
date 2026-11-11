# Bidora — Product Auction API: Backend Documentation & E2E Review

**Project:** Bidora — Online Auction Platform  
**Backend:** `ProductAuction.API` (ASP.NET Core 10)  
**Solution Root:** `D:\Sazzadul_PTSL_236_Intern\Sazzadul DotNet\ProductAuction`  
**Frontend:** `bidora-client` (Vite + React 19) at `D:\Sazzadul_PTSL_236_Intern\Sazzadul React\Bidora Project\bidora-client`  
**Status:** Documented from source code review (no test suite present)

---

## 1. Overview

Bidora is a multi-role online auction platform backend. It coordinates the full lifecycle of an auctioned product:

> seller listing & admin approval → scheduled auction activation → live bidding with wallet-backed funds → automatic result processing → buyer payment → seller dispatch → buyer receipt confirmation → dispute resolution & penalty settlement.

### Roles

| Role                | Capabilities                                                                                            |
| ------------------- | ------------------------------------------------------------------------------------------------------- |
| Buyer               | Browse auctions, place bids, buyout, win auctions, pay, confirm receipt, open disputes, deposit funds   |
| Seller              | Create/edit products, submit for approval, configure auctions, dispatch sold items, report non-delivery |
| Admin / Super Admin | Manage users, categories, products, auctions, deposit requests, disputes, platform wallet, roles        |

---

## 2. Architecture & Technology Stack

| Layer                 | Technology                                                      |
| --------------------- | --------------------------------------------------------------- |
| Framework             | ASP.NET Core 10 (Web API, minimal OpenAPI via `AddOpenApi`)     |
| ORM / Database        | Entity Framework Core 10 + Microsoft SQL Server                 |
| Identity / Auth       | ASP.NET Core Identity + JWT Bearer (HMAC SHA256, 2-hour expiry) |
| Background Processing | 4 × .NET `BackgroundService` hosted services (30s polling)      |
| File Storage          | Local disk (`wwwroot/uploads`), served via static files         |
| CORS                  | `AllowAll` (any origin, header, method)                         |
| Rate Limiting         | Fixed window on `/auth/login` only (5 req / 1 min)              |
| Serialization         | Enums as strings (`JsonStringEnumConverter`)                    |

### Project Structure (`ProductAuction.API/`)

```
Controllers/        Auth, Admin, Auction, Bid, Buyer, Category, Dispute,
                    Product, Profile, Role, Sale, Seller, Wallet
DTOs/<feature>/     Request/Response DTOs per feature
Data/               AppDbContext (IdentityDbContext), Seeder/
Migrations/         EF Core migrations (12 migrations, Oct–Nov 2026)
Models/             Domain entities + Enums/
Services/           IImageUploadService, ImageUploadService, Background/
wwwroot/uploads/    Uploaded product/NID/receipt images
Program.cs          DI, auth, CORS, rate limiting, seeding, pipeline
appsettings.json    Connection string + JWT settings (see security notes)
```

---

## 3. Database Schema (Models)

All entities use `Precision(18,2)` for monetary values. Enum columns are stored as `varchar(30)`.

| Entity              | Key Fields                                                                                                      | Notes                                                             |
| ------------------- | --------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `UserProfile`       | UserId, FullName, NationalIdNumber, NationalIdFront/BackImageUrl, VerificationStatus                            | 1:1 with IdentityUser                                             |
| `Wallet`            | UserId, Balance, ReservedBalance                                                                                | 1:1 with IdentityUser                                             |
| `Category`          | Name                                                                                                            | Product categories                                                |
| `Product`           | SellerId, CategoryId, Title, StartingPrice, BuyoutPrice, ApprovalStatus, Status                                 | Draft → PendingApproval → Approved/Rejected                       |
| `ProductImage`      | ProductId, ImageUrl, IsMain                                                                                     | Product photos                                                    |
| `Auction`           | ProductId, SellerId, StartTime, EndTime, Status, CurrentHighestBid, WinnerId, Result                            | RowVersion present (unused)                                       |
| `Bid`               | AuctionId, BidderId, Amount, IsBuyout, PlacedAt                                                                 | Bid history                                                       |
| `WalletReservation` | WalletId, ProductId?, AuctionId?, SaleId?, Type, Amount, Status                                                 | SellerDeposit, BuyerFunds, SaleEscrow                             |
| `WalletTransaction` | WalletId, Type, Amount, BalanceAfter, Reference, CreatedAt                                                      | Financial ledger                                                  |
| `Sale`              | AuctionId, ProductId, BuyerId, SellerId, FinalAmount, CommissionAmount, SellerProceeds, Status, PaymentDeadline | AwaitingPayment → Paid → Dispatched → Completed/Refunded/Disputed |
| `DeliveryRecord`    | SaleId, Status, DispatchDeadline, DispatchInfo, TrackingEvidence, ReceiptProofUrl                               | Dispatch lifecycle                                                |
| `Dispute`           | SaleId, ProductId, RaisedBy, Complaint, EvidenceUrl, Status, AdminDecision                                      | Open → UnderReview → Resolved/Rejected                            |
| `Penalty`           | DisputeId, AgainstUserId, ProductValue, TotalAmount, OppositePartyAmount, PlatformAmount                        | 80/20 split of 25% of product starting price                      |
| `DepositRequest`    | WalletId, Method, Amount, TransactionReference, ReceiptImageUrl, Status                                         | PendingVerification → Approved/Rejected                           |

### Enums

`AuctionStatus` (Upcoming/Active/Completed/Cancelled), `AuctionResult` (Sold/NoWinner), `ProductApprovalStatus` (Draft/PendingApproval/Approved/Rejected), `ProductStatus` (Draft/Available/Unavailable), `SaleStatus` (AwaitingPayment/PaymentOverdue/Paid/Dispatched/Completed/Disputed/Refunded), `DeliveryStatus` (AwaitingDispatch/Dispatched/DispatchOverdue/ReceiptConfirmed/NonDeliveryReported), `DisputeStatus` (Open/UnderReview/Resolved/Rejected), `PenaltyStatus` (Proposed/Applied), `ReservationType` (SellerDeposit/BuyerFunds/SaleEscrow), `ReservationStatus` (Reserved/Released/Forfeited), `WalletTransactionType` (Credit/Debit/Penalty/Release/Commission), `DepositRequestStatus` (PendingVerification/Approved/Rejected), `VerificationStatus` (NotSubmitted/PendingReview/Verified/Rejected).

---

## 4. API Endpoint Reference

Base route: `api/[controller]`. All timestamps UTC. Enums serialized as strings.

### 4.1 Auth (`AuthController`) — partially public

| Method | Path                    | Auth | Roles | Description                                                         |
| ------ | ----------------------- | ---- | ----- | ------------------------------------------------------------------- |
| POST   | `/auth/register`        | None | —     | Register Buyer/Seller; creates Identity user, role, profile, wallet |
| POST   | `/auth/login`           | None | —     | Rate-limited (5/min); returns JWT (2h) + roles                      |
| GET    | `/auth/test-token`      | JWT  | Any   | Token validity check                                                |
| GET    | `/auth/me`              | JWT  | Any   | Current user account + profile                                      |
| PUT    | `/auth/me`              | JWT  | Any   | Update profile (multipart); upload NID images                       |
| POST   | `/auth/change-password` | JWT  | Any   | Change password (requires current password)                         |

### 4.2 Product (`ProductController`) — seller + admin

| Method | Path                                | Auth | Roles            | Description                                 |
| ------ | ----------------------------------- | ---- | ---------------- | ------------------------------------------- |
| GET    | `/product/my-products`              | JWT  | Seller           | Paginated seller products (any status)      |
| GET    | `/product/my-products/{id}`         | JWT  | Seller           | Single owned product                        |
| POST   | `/product`                          | JWT  | Seller           | Create Draft product (multipart, ≥1 image)  |
| PUT    | `/product/{id}`                     | JWT  | Seller           | Update Draft/Rejected product (multipart)   |
| POST   | `/product/{id}/submit-for-approval` | JWT  | Seller           | Reserve 25% deposit → PendingApproval       |
| POST   | `/product/{id}/withdraw`            | JWT  | Seller           | Release deposit → Draft                     |
| DELETE | `/product/{id}`                     | JWT  | Seller           | Delete Draft/Rejected product               |
| GET    | `/product/pending-approval`         | JWT  | Admin/SuperAdmin | Paginated pending products                  |
| GET    | `/product/pending-approval/{id}`    | JWT  | Admin/SuperAdmin | Single pending product                      |
| PUT    | `/product/{id}/approval`            | JWT  | Admin/SuperAdmin | Approve/Reject; creates Auction on approval |

### 4.3 Auction (`AuctionController`) — public + seller + admin

| Method | Path                                               | Auth | Roles            | Description                                                                |
| ------ | -------------------------------------------------- | ---- | ---------------- | -------------------------------------------------------------------------- |
| GET    | `/auction`                                         | None | —                | Paginated auctions (search/filter/status)                                  |
| GET    | `/auction/active`                                  | None | —                | Currently active auctions                                                  |
| GET    | `/auction/home`                                    | None | —                | Banner + ≤8 active + ≤8 upcoming                                           |
| GET    | `/auction/{id}`                                    | None | —                | Auction details                                                            |
| GET    | `/auction/{id}/seller`                             | JWT  | Any              | Auction details (seller view; no ownership check on route)                 |
| GET    | `/auction/product/{productId}`                     | None | —                | All auctions for a product                                                 |
| PUT    | `/auction/{id}/configure`                          | JWT  | Admin/SuperAdmin | Set StartTime/EndTime on Upcoming auction                                  |
| PUT    | `/auction/{id}/cancel`                             | JWT  | Any              | Seller cancels Upcoming (deposit forfeited); Admin cancels Upcoming/Active |
| POST   | `/auction/product/{productId}/reset-for-reauction` | JWT  | Seller           | Reset eligible product to Draft for re-auction                             |

> **Note:** `GetUpcomingAuctions()` (lines 298–335) is an unrouted public method with no `[HttpGet]` attribute — unreachable dead code.

### 4.4 Bid (`BidController`) — buyer only

| Method | Path                                  | Auth | Roles            | Description                                          |
| ------ | ------------------------------------- | ---- | ---------------- | ---------------------------------------------------- |
| POST   | `/bid`                                | JWT  | Buyer            | Place bid (reserves 25% starting price on first bid) |
| GET    | `/bid/auction/{auctionId}/bids`       | JWT  | Any              | Public bid history for an auction                    |
| GET    | `/bid/auction/{auctionId}/bids/admin` | JWT  | Admin/SuperAdmin | Admin bid history with bidder emails                 |
| POST   | `/bid/{auctionId}/buyout`             | JWT  | Buyer            | Instant win at buyout price; creates Sale            |

### 4.5 Sale (`SaleController`) — buyer + seller + admin

| Method | Path                             | Auth | Roles            | Description                                                      |
| ------ | -------------------------------- | ---- | ---------------- | ---------------------------------------------------------------- |
| GET    | `/sale`                          | JWT  | Admin/SuperAdmin | All sales (admin)                                                |
| GET    | `/sale/my-sales`                 | JWT  | Any              | Sales where user is buyer or seller                              |
| POST   | `/sale/{id}/pay`                 | JWT  | Buyer            | Pay full amount (wallet debit → escrow; 5-day dispatch deadline) |
| POST   | `/sale/{id}/dispatch`            | JWT  | Seller           | Record dispatch with tracking image (5-day deadline)             |
| POST   | `/sale/{id}/report-non-delivery` | JWT  | Seller           | Report non-delivery (after 10-day pickup window) → Disputed      |
| POST   | `/sale/{id}/confirm-receipt`     | JWT  | Buyer            | Confirm receipt with proof image; releases escrow + deposit      |

### 4.6 Wallet (`WalletController`) — authenticated

| Method | Path                       | Auth | Roles | Description                               |
| ------ | -------------------------- | ---- | ----- | ----------------------------------------- |
| GET    | `/wallet/me`               | JWT  | Any   | Balance / reserved / available            |
| POST   | `/wallet/deposit-request`  | JWT  | Any   | Submit deposit (multipart, receipt image) |
| GET    | `/wallet/deposit-requests` | JWT  | Any   | User's deposit requests                   |
| GET    | `/wallet/transactions`     | JWT  | Any   | User's wallet transactions                |
| GET    | `/wallet/reservations`     | JWT  | Any   | User's wallet reservations                |

### 4.7 Dispute (`DisputeController`) — buyer/seller + admin

| Method | Path                    | Auth | Roles            | Description                                              |
| ------ | ----------------------- | ---- | ---------------- | -------------------------------------------------------- |
| GET    | `/dispute`              | JWT  | Admin/SuperAdmin | All disputes with penalties, counts                      |
| POST   | `/dispute`              | JWT  | Any              | Buyer/seller submits dispute (multipart, evidence image) |
| PUT    | `/dispute/{id}/resolve` | JWT  | Admin/SuperAdmin | Resolve with fault type + optional penalty               |

### 4.8 Profile (`ProfileController`) — authenticated

| Method | Path                            | Auth | Roles            | Description                             |
| ------ | ------------------------------- | ---- | ---------------- | --------------------------------------- |
| PUT    | `/profile/submit-verification`  | JWT  | Any              | Submit NID verification → PendingReview |
| GET    | `/profile/pending-verification` | JWT  | Admin/SuperAdmin | Pending verification profiles           |
| PUT    | `/profile/{id}/verify`          | JWT  | Admin/SuperAdmin | Verify/Reject profile                   |

### 4.9 Admin (`AdminController`) — admin staff

| Method | Path                                  | Auth | Roles                                   | Description                                |
| ------ | ------------------------------------- | ---- | --------------------------------------- | ------------------------------------------ |
| GET    | `/admin/dashboard`                    | JWT  | Admin/SuperAdmin                        | Aggregated stats                           |
| POST   | `/admin/create-admin`                 | JWT  | SuperAdmin (for Admin/SuperAdmin roles) | Create staff account                       |
| PUT    | `/admin/user/{id}`                    | JWT  | Admin/SuperAdmin                        | Edit staff account                         |
| PUT    | `/admin/user/{id}/reset-password`     | JWT  | Any (blocked for Buyer/Seller targets)  | Reset staff password                       |
| GET    | `/admin/user`                         | JWT  | Admin/SuperAdmin                        | All users (+role filter)                   |
| PUT    | `/admin/user/{id}/lock`               | JWT  | Admin/SuperAdmin                        | Lock/unlock account                        |
| GET    | `/admin/auctions`                     | JWT  | Admin/SuperAdmin                        | All auctions                               |
| POST   | `/admin/disputes/{id}/start-review`   | JWT  | Admin/SuperAdmin                        | Open → UnderReview                         |
| GET    | `/admin/deposit-requests`             | JWT  | Admin/SuperAdmin                        | Deposit requests                           |
| PUT    | `/admin/deposit-requests/{id}/review` | JWT  | Admin/SuperAdmin                        | Approve/Reject (credits wallet on approve) |
| GET    | `/admin/transactions`                 | JWT  | Admin/SuperAdmin                        | All wallet transactions                    |
| GET    | `/admin/reservations`                 | JWT  | Admin/SuperAdmin                        | All reservations                           |
| GET    | `/admin/platform-wallet`              | JWT  | Admin/SuperAdmin                        | Platform wallet + seller/buyer totals      |
| GET    | `/admin/user/role-counts`             | JWT  | Admin/SuperAdmin                        | Counts per role                            |

### 4.10 Category (`CategoryController`) — public read, admin write

| Method | Path             | Auth | Roles            | Description     |
| ------ | ---------------- | ---- | ---------------- | --------------- |
| GET    | `/category`      | None | —                | Categories      |
| POST   | `/category`      | JWT  | Admin/SuperAdmin | Create category |
| DELETE | `/category/{id}` | JWT  | Admin/SuperAdmin | Delete category |

### 4.11 Role (`RoleController`) — admin staff

| Method              | Path                  | Auth | Roles            | Description                                          |
| ------------------- | --------------------- | ---- | ---------------- | ---------------------------------------------------- |
| GET/PUT/POST/DELETE | `/admin/roles[/{id}]` | JWT  | Admin/SuperAdmin | CRUD roles (Admin/SuperAdmin/Buyer/Seller protected) |

### 4.12 Buyer / Seller dashboards

- `GET /buyer/dashboard` — active/leading bids, won count, awaiting payment, total spent, wallet
- `GET /buyer/my-bids` — paginated buyer bids
- `GET /buyer/won-auctions` — won auctions with sale/dispute/delivery info
- `GET /seller/dashboard` — product counts, active auctions, sold/awaiting dispatch, earnings
- `GET /seller/sales` (via `GET /sale/my-sales`) — seller's sales

---

## 5. Background Services (4 × 30s polling)

| Service                            | Purpose                                                                                                                                                                                                          |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AuctionStatusBackgroundService`   | Flips Upcoming → Active when `StartTime` arrives                                                                                                                                                                 |
| `AuctionResultBackgroundService`   | Processes ended auctions: determines winner (highest bid), creates `Sale` (AwaitingPayment, 3-day deadline), releases non-winner buyer reservations; NoWinner releases seller deposit and sets product Available |
| `PaymentOverdueBackgroundService`  | Flips AwaitingPayment → PaymentOverdue when `PaymentDeadline` passes                                                                                                                                             |
| `DispatchOverdueBackgroundService` | Flips AwaitingDispatch → DispatchOverdue when `DispatchDeadline` passes                                                                                                                                          |

> All four services catch exceptions per-iteration and continue; no distributed lock, so a single instance is assumed.

---

## 6. E2E Business Workflows

### W1. Product listing & approval

Seller creates Draft product (+images) → submits for approval (25% deposit reserved) → Admin approves (creates Upcoming Auction; product Available) or Rejects (deposit released).

### W2. Auction lifecycle

Admin configures Start/End time → background activates at StartTime → buyers place bids → background processes at EndTime (winner/NoWinner) → Sale created (AwaitingPayment).

### W3. Bidding

Buyer bids on Active auction (≥ starting price, meets increment) → 25% starting price reserved → bid recorded → auction highest-bid updated.

### W4. Buyout

Buyer buys at buyout price → auction Completed/Sold → Sale created → product Unavailable → non-winner reservations released; winner's reservation held until payment.

### W5. Payment

Buyer pays Sale (wallet debit, full amount) → escrow reserved → Sale Paid → DeliveryRecord AwaitingDispatch (5-day deadline). Missed deadline → PaymentOverdue (buyer's BuyerFunds reservation is **never released** — see Flaw F6).

### W6. Dispatch & receipt

Seller dispatches (+tracking image, 5-day deadline) → Sale Dispatched → buyer confirms receipt (+proof image) → seller credited proceeds, platform credited commission, escrow + deposit released → Sale Completed.

### W7. Non-delivery & dispute

Seller reports non-delivery (after 10-day pickup window) → Sale Disputed → either party can open dispute (evidence image) → Admin starts review → resolves with fault type (Buyer/Seller/System) and optional penalty (80/20 split of 25% product value) → Sale Refunded, product Unavailable.

### W8. Wallet / deposit

User submits deposit request (+receipt image) → Admin approves (wallet credited) or rejects.

### W9. Re-auction

Eligible product (NoWinner or PaymentOverdue sale) → seller resets to Draft → re-submit for approval → new auction.

---

## 7. Authentication & Security Model

- JWT issued on login, 2-hour expiry, HMAC-SHA256, issuer/audience validated.
- Claims: `NameIdentifier` (user Id), `Email`, one `Role` claim per role.
- Role-based `[Authorize(Roles=...)]` on sensitive endpoints.
- Password reset via Identity tokens; lockout supported.
- No refresh token / rotation; no email confirmation enforced.
- JWT signing key read from config (`Jwt:Key`).

---

## 8. E2E Flaw Analysis — Major Issues Found

The following are the most significant flaws identified by walking the end-to-end flows against the backend source.

### F1 — JWT signing key committed in source (CRITICAL)

**File:** `ProductAuction.API/appsettings.json:10`  
The JWT signing key `9582240cb3b14f62d75927e5179c573bc5c5f469` is committed in plaintext. The README (`ReadMe:218`) explicitly warns against committing keys, yet it is in the repo. Anyone with repository access can forge valid admin tokens.  
**Recommendation:** Remove from `appsettings.json`; use User Secrets / environment variables in production; rotate the key immediately.

### F2 — Overly permissive CORS (HIGH)

**File:** `ProductAuction.API/Program.cs:27–32`  
`AllowAll` permits any origin, header, and method. Combined with JWT stored in browser `localStorage` (frontend), this increases XSS/CSRF exposure.  
**Recommendation:** Restrict to known frontend origins; consider `AllowCredentials` only with specific origins.

### F3 — JWT in localStorage, no refresh/rotation (HIGH)

**File:** `bidora-client/src/services/authApi.js:58–75`  
Token and roles are written to `localStorage` and read on every request. There is no refresh-token mechanism, no automatic 401 → redirect-to-login handling, and tokens live 2 hours with no rotation. XSS can steal the token and impersonate the user for the full window.  
**Recommendation:** Use HttpOnly, SameSite cookies; add a refresh endpoint and 401 interceptor.

### F4 — No email confirmation enforcement (HIGH)

**File:** `ProductAuction.API/Controllers/AuthController.cs:68–115`  
Registration creates users with `EmailConfirmed = false` but nothing checks `EmailConfirmed` before granting access. Unverified email addresses can bid, pay, and sell.  
**Recommendation:** Require email confirmation before allowing wallet-funded actions (bidding, buying, selling).

### F5 — Bid race condition / no optimistic concurrency (HIGH)

**File:** `ProductAuction.API/Controllers/BidController.cs:45–208`  
`PlaceBid` reads `auction.CurrentHighestBid`, then writes `auction.CurrentHighestBid = dto.Amount` and `auction.BidCount++` without checking whether another bid arrived in between. The `Auction.RowVersion` property exists (`Models/Auction.cs:31`) but is never used for concurrency checking. Concurrent bids can both succeed or overwrite each other.  
**Recommendation:** Use `RowVersion` / `ConcurrencyCheck` and re-read-validate inside the transaction.

### F6 — Buyer reservation never released on payment overdue (HIGH)

**File:** `ProductAuction.API/Services/Background/PaymentOverdueBackgroundService.cs` + `SaleController.cs:363–387`  
When a sale becomes `PaymentOverdue` (deadline passed, or buyer attempts late payment), the winning buyer's `BuyerFunds` `WalletReservation` remains `Reserved` forever. The funds are locked with no release path. Re-auction (`reset-for-reauction`) allows relisting but does not clear the stale reservation.  
**Recommendation:** In the overdue path, release the winner's `BuyerFunds` reservation (or convert/forfeit it per policy) and document buyer consequences.

### F7 — Buyout only reserves 25%, not full sale amount (MEDIUM)

**File:** `ProductAuction.API/Controllers/BidController.cs:431–574`  
Buyout reserves only 25% of `StartingPrice` at bid time, but the sale `FinalAmount` is the full buyout price. A buyer with insufficient funds can win via buyout, then fail at `/sale/{id}/pay`, leaving the sale stuck in `AwaitingPayment` until the background service marks it overdue (see F6).  
**Recommendation:** Reserve the full buyout amount at buyout time, or validate full affordability before allowing buyout.

### F8 — No rate limiting beyond login (MEDIUM)

**File:** `ProductAuction.API/Program.cs:47–55`  
Only `/auth/login` is rate-limited. Registration, bidding, buyout, payment, dispatch, and deposit submission are unthrottled, enabling abuse/brute-force on wallet actions.  
**Recommendation:** Add rate limiting per-IP/user for write endpoints (bid, pay, dispatch, deposit).

### F9 — Admin `GetAllUsers` loads all profiles & wallets into memory (MEDIUM)

**File:** `ProductAuction.API/Controllers/AdminController.cs:440–441`  
`_context.UserProfiles.ToListAsync()` and `_context.Wallets.ToListAsync()` load every row before paging, causing O(n) memory and N+1-ish behavior as data grows. Similar nested subqueries in `GetAllTransactions` (`AdminController.cs:1043–1059`).  
**Recommendation:** Page server-side; avoid materializing full tables.

### F10 — Dead code: unrouted `GetUpcomingAuctions` (LOW)

**File:** `ProductAuction.API/Controllers/AuctionController.cs:298–335`  
Public method lacking an `[HttpGet]` attribute; cannot be invoked via HTTP.  
**Recommendation:** Remove or add route attribute.

### F11 — Swagger/OpenAPI configured but no UI (LOW)

**File:** `ProductAuction.API/Program.cs:82` + `.csproj:19`  
`AddOpenApi()` emits `/openapi/v1.json` but Swashbuckle (`Swashbuckle.AspNetCore`) is referenced yet `AddSwaggerGen`/`UseSwaggerUI` are not called, so there is no interactive API docs UI despite the README listing Swagger as a feature.  
**Recommendation:** Configure Swagger UI or remove the unused package reference.

### F12 — Image upload has no antivirus / content validation beyond extension (LOW)

**File:** `ProductAuction.API/Services/ImageUploadService.cs:25–33`  
Only extension (jpg/jpeg/png/webp) and 5 MB size are validated. File contents are not scanned; a malicious file with a valid extension could be stored and served statically.  
**Recommendation:** Add content-type validation and virus scanning; serve uploads from a separate domain.

### F13 — Dispute raised on PaymentOverdue sales allowed (LOW)

**File:** `ProductAuction.API/Controllers/DisputeController.cs:254–269`  
Only `AwaitingPayment` and `Completed` are blocked. A buyer who never paid (sale already `PaymentOverdue`) can still open a dispute, which is logically inconsistent.  
**Recommendation:** Block disputes on `PaymentOverdue` as well.

### F14 — Unused model fields (LOW)

`Sale.PaymentConfirmedAt` / `PaymentConfirmedBy` (`Models/Sale.cs:29–31`) are never populated. `WalletTransaction.BalanceAfter` is recorded but not audited for consistency.  
**Recommendation:** Populate or remove.

### F15 — Frontend targets an internal IP (LOW)

**File:** `bidora-client/.env:3` and `vite.config.js:12`  
`VITE_API_BASE_URL=http://10.192.168.204:5102/api` is an internal LAN address; the app will not work outside that network.  
**Recommendation:** Use environment-appropriate URLs; document deployment config.

### Summary severity table

| ID      | Severity | Area                                  |
| ------- | -------- | ------------------------------------- |
| F1      | Critical | Security (committed JWT key)          |
| F2      | High     | Security (CORS)                       |
| F3      | High     | Security (token storage / no refresh) |
| F4      | High     | Security (no email confirmation)      |
| F5      | High     | Concurrency (bid race)                |
| F6      | High     | Data integrity (stale reservation)    |
| F7      | Medium   | Business logic (buyout affordability) |
| F8      | Medium   | Abuse protection (rate limiting)      |
| F9      | Medium   | Performance (admin queries)           |
| F10–F15 | Low      | Code quality / UX                     |

---

## 9. Configuration

| Setting                               | Source                     | Notes                                            |
| ------------------------------------- | -------------------------- | ------------------------------------------------ |
| `ConnectionStrings:DefaultConnection` | User Secrets / appsettings | SQL Server (LocalDB or SQLEXPRESS)               |
| `Jwt:Key`                             | User Secrets (should be)   | Currently committed in appsettings.json — rotate |
| `Jwt:Issuer`                          | `ProductAuction.API`       |                                                  |
| `Jwt:Audience`                        | `ProductAuction.React`     |                                                  |
| `VITE_API_BASE_URL`                   | Frontend `.env`            | Points to `10.192.168.204:5102/api`              |

### Setup

1. .NET 10 SDK + SQL Server.
2. `cd ProductAuction.API` and `dotnet user-secrets init`; set connection string + JWT key.
3. `dotnet ef database update` (requires `dotnet-ef` tool).
4. `dotnet run` — seeds roles, admin account, and platform system account on startup.

---

## 10. Testing Status

- No unit, integration, or e2e test project exists in either the frontend or backend repository (`**/*Test*` and `**/*.{test,spec}*` returned no results).
- The `ReadMe` references `Scripts/Test-Endpoints.ps1` (a manual PowerShell smoke script) and records "48 passing smoke tests" from 29 Oct 2026, but these are historical and not part of the current checkout.
- The `ProductAuction.API.http` file contains only a placeholder weatherforecast request — not a real API test suite.

**Recommendation:** Add an integration test project (xUnit + `WebApplicationFactory`) covering auth, product approval, bidding, buyout, payment, dispatch, receipt, and dispute flows before production release.

---

## 11. Related Files

- Backend README: `D:\Sazzadul_PTSL_236_Intern\Sazzadul DotNet\ProductAuction\ReadMe`
- Dispute resolution notes: `D:\Sazzadul_PTSL_236_Intern\Sazzadul DotNet\ProductAuction\ProductAuction.API\Controllers\Dispute Resolution.md`
- Work log: `D:\Sazzadul_PTSL_236_Intern\Sazzadul DotNet\ProductAuction\WorkLog.md`
- Endpoint smoke script: `D:\Sazzadul_PTSL_236_Intern\Sazzadul DotNet\ProductAuction\Scripts\Test-Endpoints.ps1`
- Frontend README: `D:\Sazzadul_PTSL_236_Intern\Sazzadul React\Bidora Project\bidora-client\README.md`
- Three-party dispute discussion: `D:\Sazzadul_PTSL_236_Intern\Sazzadul DotNet\ProductAuction\ProductAuction — Three-Party Dispute Discussion System.md`
