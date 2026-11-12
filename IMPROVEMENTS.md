# Bidora — Improvement Backlog

Low-effort, high-value improvements that do **not** require a major overhaul. Grouped by area, with priority and rough effort.

Priority: **P0** = do first (security/correctness), **P1** = soon, **P2** = nice to have.
Effort: **S** = minutes, **M** = ~1 hour, **L** = a few hours.

---

## Backend

- [ ] **Move secrets out of `appsettings.json`** (P0, S) — JWT key and DB connection string are committed. Use user-secrets/environment variables and rotate the key.
- [ ] **Global exception handler** (P0, M) — `Program.cs` only has `UseDeveloperExceptionPage`; unhandled 500s return raw output, not the standardized envelope. Add `UseExceptionHandler` returning `{ status: 500, message }`.
- [ ] **Configure `OnModelCreating` in `AppDbContext`** (P0, M) — currently absent. Add unique index on `Wallets.UserId`, indexes on `Auctions.Status`, `Sales.BuyerId/SellerId`, `Bids.AuctionId`, `Disputes.Status`; set FK delete behavior to `Restrict` where needed.
- [x] **Use `AsNoTracking()` on read-only queries** (P1, S) — added to EF queries in all GET/read-only endpoints; write methods left tracked. *(done)*
- [ ] **Restrict CORS from `AllowAll`** (P1, S) — set explicit origins for production.
- [ ] **Global rate limiting on write endpoints** (P1, M) — only `/auth/login` is limited; add limits to bid, buyout, pay, deposit-request.
- [ ] **Enable HTTPS redirection + HSTS** (P1, S) — `app.UseHttpsRedirection()` and HSTS outside Development.
- [ ] **Pass `CancellationToken` into EF async calls** (P2, M) — clean request aborts on client disconnect.
- [ ] **Audit DTO validation attributes** (P1, M) — `[ApiController]` only auto-400s when attributes exist; add `[Required]/[Range]` to amount/price DTOs missing them.

## Frontend

- [x] **Set `DEBUG = false` in `authApi.js`** (P0, S) — now `import.meta.env.DEV` (logs in dev only, silent in production builds). *(done)*
- [x] **Add an `ErrorBoundary` around `<Routes>`** (P1, M) — `components/ErrorBoundary/ErrorBoundary.jsx` wraps routes in `App.jsx`; shows a fallback with Reload / Go home (dev-only error detail). *(done)*
- [x] **Add `.env.example`** (P2, S) — `.env` now documented via `.env.example`; `.gitignore` ignores `.env`/`.env.*` but keeps the example tracked. *(done)*
- [x] **Centralize API error messages** (P2, S) — `utils/apiError.js` (`getErrorMessage`) + `toast.errorFrom(error, fallback)`; call sites migrated. *(done)*
- [x] **Cancel in-flight requests on unmount** (P2, M) — `AbortController` on AuctionDetails fetch + 4s bid polling; `getAuctionById`/`getAuctionBids` accept an Axios `signal`. *(done)*

## Data / Correctness

- [ ] **One wallet per user** — enforce with a unique index (covered by DbContext change above).
- [ ] **One main image per product** — enforce at write time (DB or service).

## DevEx / CI

- [ ] **GitHub Actions** (P2, M) — build backend (`dotnet build`) and frontend (`npm run build`) on PR.
- [ ] **`.editorconfig`** (P2, S) — consistent formatting across both projects.
- [ ] **`global.json`** (P2, S) — pin the .NET SDK version.

---

## Suggested order

1. Backend: secrets, exception handler, DbContext indexes.
2. Frontend: `DEBUG = false`, ErrorBoundary.
3. Rate limiting, CORS, HTTPS/HSTS, DTO validation.
4. CI + tooling.