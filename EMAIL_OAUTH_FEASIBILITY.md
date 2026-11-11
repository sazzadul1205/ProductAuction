# Feasibility Study — Email Confirmation, OAuth Login & Mail-Based Notifications

**Project:** Bidora — Product Auction API (`ProductAuction.API`)  
**Target:** ASP.NET Core 10 + EF Core 10 + SQL Server + ASP.NET Core Identity  
**Status:** Feasibility assessment (no implementation yet)

---

## 1. Scope

This study evaluates integrating three email-centric capabilities into the existing backend:

| Feature                      | Description                                                                                                                       |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| **Email Confirmation**       | Verify a user's email address before granting full account access (registration → confirmation email → click link → confirm)      |
| **OAuth Login**              | Allow users to sign in with external providers (Google, Microsoft, Facebook, etc.) via OAuth 2.0 / OpenID Connect                 |
| **Mail-Based Notifications** | Automated transactional emails for key platform events (bid won, payment due, dispatch, dispute, deposit approved/rejected, etc.) |

---

## 2. Current State Assessment

### What already exists

| Component            | Implementation                                                                              |
| -------------------- | ------------------------------------------------------------------------------------------- |
| User registration    | `AuthController.Register` — creates `IdentityUser`, role, `UserProfile`, `Wallet`           |
| Login                | `AuthController.Login` — JWT Bearer, 2-hour expiry, rate-limited (5/min)                    |
| JWT                  | `appsettings.json` contains `Jwt:Key`, `Issuer`, `Audience` (currently committed plaintext) |
| Password reset       | `AdminController.ResetUserPassword` (staff only) + Identity `ChangePasswordAsync`           |
| Email infrastructure | **None** — no `IEmailSender`, no SMTP/SendGrid config, no email templates                   |
| OAuth                | **None** — no external login providers configured                                           |
| Notifications        | **None** — no email or push notification system                                             |

### Gaps to fill

1. **No email sender abstraction** — Identity's `IEmailSender` is not implemented.
2. **No email confirmation flow** — Users are created with `EmailConfirmed = false` but nothing enforces it.
3. **No OAuth providers** — Only local username/password auth is supported.
4. **No notification service** — No code triggers emails on business events.

---

## 3. Feasibility Analysis

### 3.1 Email Confirmation

**Verdict: HIGH feasibility, low complexity**

| Aspect        | Assessment                                                                                                                                     |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Technical fit | ASP.NET Core Identity natively supports email confirmation (`EmailConfirmed` flag, `GenerateEmailConfirmationTokenAsync`, `ConfirmEmailAsync`) |
| Backend work  | Implement `IEmailSender`, add confirmation endpoint, gate sensitive actions behind `EmailConfirmed`                                            |
| Frontend work | Add "Confirm your email" page, resend confirmation link, update registration flow                                                              |
| Dependencies  | SMTP provider (SendGrid, Mailgun, AWS SES, or local SMTP for dev)                                                                              |
| Risk          | Low — well-documented Identity pattern                                                                                                         |

**Approach:**

1. Register email sender service (`IEmailSender`) in `Program.cs`.
2. After `Register`, generate confirmation token via `UserManager.GenerateEmailConfirmationTokenAsync(user)` and send it in an email with a confirm link (`/auth/confirm-email?userId=...&token=...`).
3. Add `[HttpGet] /auth/confirm-email` endpoint to call `UserManager.ConfirmEmailAsync(user, token)`.
4. Gate wallet/bid/payment endpoints behind `user.EmailConfirmed`.

### 3.2 OAuth Login

**Verdict: MEDIUM-HIGH feasibility, medium complexity**

| Aspect        | Assessment                                                                                                                     |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Technical fit | ASP.NET Core Authentication has built-in `AddGoogle`, `AddMicrosoftAccount`, `AddFacebook`, etc.                               |
| Backend work  | Configure external providers in `Program.cs`, add `/auth/external-login` callback, map external claims to local `IdentityUser` |
| Frontend work | Add "Sign in with Google/Microsoft" buttons, handle redirect flow                                                              |
| Dependencies  | OAuth client ID/secret from each provider (Google Cloud Console, Azure AD, Facebook Developer)                                 |
| Risk          | Medium — user account linking (merge external identity with existing local account) needs care                                 |

**Approach:**

1. Install `Microsoft.AspNetCore.Authentication.Google` (and/or Microsoft/Facebook).
2. Configure in `Program.cs` with `ClientId`/`ClientSecret` from User Secrets.
3. Add challenge endpoint that redirects to provider, then callback that:
   - Looks up existing user by email (from provider claims).
   - If exists, signs them in with local JWT.
   - If not, optionally auto-creates an account or prompts to link.

### 3.3 Mail-Based Notifications

**Verdict: HIGH feasibility, medium complexity**

| Aspect        | Assessment                                                                                                                      |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Technical fit | Straightforward — inject `IEmailSender` into controllers/services and call on business events                                   |
| Backend work  | Create notification service, define email templates, wire into key endpoints (payment, dispatch, dispute, deposit, auction end) |
| Frontend work | Minimal — possibly a notification center page, but emails are server-driven                                                     |
| Dependencies  | Same email sender as confirmation; template engine (Razor Light or plain HTML strings)                                          |
| Risk          | Low — fire-and-forget emails; failures shouldn't block business transactions                                                    |

**Approach:**

1. Create `INotificationService` / `EmailNotificationService` wrapping `IEmailSender`.
2. Define templates for: welcome, email confirmation, password reset, bid outbid, payment due reminder, dispatch reminder, dispute opened/resolved, deposit approved/rejected, auction ended.
3. Call notification service from relevant controllers/background services (non-blocking; log failures, don't roll back transactions).

---

## 4. Recommended Tech Stack

| Concern               | Recommendation                                                           | Reason                                               |
| --------------------- | ------------------------------------------------------------------------ | ---------------------------------------------------- |
| Email provider (dev)  | Local SMTP (MailHog / Papercut) or .NET `SmtpClient` to pickup directory | No cost, no real emails during dev                   |
| Email provider (prod) | SendGrid, Mailgun, or AWS SES                                            | High deliverability, API-driven, good .NET libraries |
| Email templates       | Razor Light or embedded HTML strings                                     | Simple, maintainable                                 |
| OAuth providers       | Google + Microsoft (start), Facebook later                               | Most auction users have Google/Microsoft accounts    |
| Token storage         | User Secrets (dev), Environment Variables / Azure Key Vault (prod)       | Never commit client secrets                          |

---

## 5. Implementation Roadmap

### Phase 1 — Email Infrastructure (Week 1)

1. Add `Services/Email/EmailSender.cs` implementing `IEmailSender`.
2. Add `appsettings.json` section:
   ```json
   "Email": {
     "SmtpHost": "...",
     "SmtpPort": 587,
     "SmtpUser": "...",
     "SmtpPass": "...",
     "FromEmail": "noreply@bidora.com",
     "FromName": "Bidora",
     "BaseUrl": "https://app.bidora.com"
   }
   ```
3. Register `IEmailSender` as scoped in `Program.cs`.
4. Add `DTOs/Auth/ConfirmEmailDto.cs` and `DTOs/Auth/ResendConfirmationDto.cs`.

### Phase 2 — Email Confirmation Flow (Week 1–2)

1. In `Register`, after user creation: generate token, send confirmation email, return "please check your email".
2. Add `GET /auth/confirm-email?userId=&token=` → `UserManager.ConfirmEmailAsync`.
3. Add `POST /auth/resend-confirmation`.
4. Gate endpoints: check `user.EmailConfirmed` before allowing bid, buyout, pay, product creation. Return 403 with "Please confirm your email" if not confirmed.

### Phase 3 — OAuth Login (Week 2–3)

1. Install `Microsoft.AspNetCore.Authentication.Google` (+ Microsoft/Facebook as needed).
2. Configure in `Program.cs`:
   ```csharp
   builder.Services.AddAuthentication()
       .AddGoogle(options => { ... })
       .AddMicrosoftAccount(options => { ... });
   ```
3. Add `GET /auth/external-login?provider=google` challenge endpoint.
4. Add callback endpoint to handle provider response, find/link local user, issue local JWT.
5. Frontend: add provider sign-in buttons.

### Phase 4 — Mail-Based Notifications (Week 3–4)

1. Create `Services/Notifications/NotificationService.cs`.
2. Define email templates (HTML).
3. Wire into:
   - Registration → welcome email
   - Email confirmation → confirmation email
   - Bid placed / outbid → notification to bidders
   - Auction ended → winner notified to pay
   - Payment overdue → reminder
   - Dispatch → buyer notified with tracking
   - Receipt confirmed → seller notified
   - Dispute opened/resolved → both parties
   - Deposit approved/rejected → user notified
4. Make notification calls fire-and-forget (log errors, don't fail the business transaction).

---

## 6. Risks & Mitigations

| Risk                                           | Impact | Mitigation                                                                                    |
| ---------------------------------------------- | ------ | --------------------------------------------------------------------------------------------- |
| Emails land in spam                            | High   | Use reputable provider (SendGrid/SES), configure SPF/DKIM/DMX, warm up IP                     |
| OAuth account linking conflicts                | Medium | Link by email; if email already exists with local password, prompt to sign in first then link |
| Blocking business logic on email send          | Low    | Make notifications async/fire-and-forget; log and continue                                    |
| Committed secrets                              | High   | Move `Jwt:Key`, OAuth secrets, SMTP credentials to User Secrets / env vars                    |
| Email confirmation adds friction to onboarding | Medium | Allow browsing/bidding as unconfirmed; only confirm before payment/selling                    |

---

## 7. Files to Modify / Create

### Backend (`ProductAuction.API/`)

| File                                            | Action                                                                                                            |
| ----------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `Program.cs`                                    | Register `IEmailSender`, configure OAuth providers, email options                                                 |
| `appsettings.json`                              | Add `Email:` section (secrets via User Secrets)                                                                   |
| `Services/Email/EmailSender.cs`                 | **Create** — SMTP/SendGrid implementation of `IEmailSender`                                                       |
| `Services/Notifications/NotificationService.cs` | **Create** — template-based notification service                                                                  |
| `DTOs/Auth/ConfirmEmailDto.cs`                  | **Create**                                                                                                        |
| `DTOs/Auth/ResendConfirmationDto.cs`            | **Create**                                                                                                        |
| `Controllers/AuthController.cs`                 | Add `ConfirmEmail`, `ResendConfirmation`; gate actions on `EmailConfirmed`; add external login challenge/callback |
| `Controllers/AuthController.cs` (Register)      | Send confirmation email after user creation                                                                       |
| `Models/`                                       | Possibly add `ExternalLogin` table if linking multiple providers per user                                         |

### Frontend (`bidora-client/`)

| File                       | Action                                                    |
| -------------------------- | --------------------------------------------------------- |
| `src/services/authApi.js`  | Add `confirmEmail`, `resendConfirmation`, `externalLogin` |
| `src/pages/Auth/`          | Add `ConfirmEmail`, `ResendConfirmation` pages            |
| `src/pages/Auth/Login.jsx` | Add OAuth provider buttons                                |
| `App.jsx`                  | Add routes for confirm/resend pages                       |

---

## 8. Estimated Effort

| Phase                                  | Effort         | Complexity |
| -------------------------------------- | -------------- | ---------- |
| Email infrastructure + confirmation    | 3–5 days       | Low–Medium |
| OAuth login (Google + Microsoft)       | 3–5 days       | Medium     |
| Mail-based notifications (core events) | 4–6 days       | Medium     |
| **Total**                              | **~2–3 weeks** |            |

---

## 9. Recommendation

Proceed with all three features. They are well-supported by ASP.NET Core Identity and authentication middleware, carry low technical risk, and directly address security gaps identified in the prior e2e review (F3: JWT in localStorage with no email confirmation). Start with **email confirmation** first, as it's the foundation for both OAuth account linking and trustworthy notifications.
