# ProductAuction — Role Management & Access Control System

## Backend Feasibility Study & Implementation Specification

## 1. Objective

Evaluate and implement a flexible role management and access control system in the ProductAuction backend.

The goal is to make it possible to:

- Add new user roles.
- Manage existing roles.
- Assign roles to users.
- Remove roles from users.
- Control which API operations each role can access.
- Restrict sensitive operations to authorized users.
- Allow administrators to manage access without modifying application code for every new role.

The implementation must work with the existing ASP.NET Core Identity authentication system.

**Important:** This feature is about backend authorization and role management, not just displaying different menus or pages in the frontend.

---

## 2. Existing System Analysis

Before implementation, inspect the existing authentication and authorization configuration.

Known project architecture includes:

- ASP.NET Core Web API.
- .NET 10.
- `AddIdentity<IdentityUser, IdentityRole>()`.
- Entity Framework Core Identity integration.
- SQL Server.
- Existing authentication and authorization middleware.

Inspect the following:

- `Program.cs`
- `AppDbContext.cs`
- Existing Identity configuration.
- `ApplicationUser`, if present.
- Existing `IdentityRole` usage.
- Authentication and JWT configuration.
- Existing Admin, Buyer, and Seller authorization.
- Existing controllers and authorization attributes.
- Existing user registration and role assignment logic.
- Existing database migrations.

Determine whether the current project already has role management endpoints or role-related services.

Do not create duplicate Identity functionality.

---

## 3. Understand Roles and Permissions

The system should distinguish between roles and permissions.

### 3.1 Roles

A role represents a user's responsibility within the marketplace.

Examples:

| Role           | Purpose                                         |
| -------------- | ----------------------------------------------- |
| Admin          | Full administrative access                      |
| Buyer          | Purchase and bidding operations                 |
| Seller         | Product and auction management                  |
| Moderator      | Reviews reports and moderates platform activity |
| DisputeOfficer | Handles dispute-related operations              |
| SupportStaff   | Handles customer support                        |
| FinanceStaff   | Reviews financial transactions                  |

These are examples of possible roles, not instructions to create every role immediately.

The existing Admin, Buyer, and Seller roles must remain compatible with current functionality.

### 3.2 Permissions

A permission represents a specific action a user is allowed to perform.

Examples:

| Permission          | Description                 |
| ------------------- | --------------------------- |
| `Products.View`     | View products               |
| `Products.Create`   | Create products             |
| `Products.Update`   | Update products             |
| `Products.Delete`   | Delete products             |
| `Auctions.View`     | View auctions               |
| `Auctions.Manage`   | Manage auctions             |
| `Disputes.View`     | View disputes               |
| `Disputes.Resolve`  | Resolve disputes            |
| `Users.View`        | View user information       |
| `Users.Manage`      | Manage users                |
| `Wallets.View`      | View wallet information     |
| `Transactions.View` | View transactions           |
| `Reports.View`      | View administrative reports |

A role can have multiple permissions.

A permission can be assigned to multiple roles.

Example:

| Role           | Permissions                                     |
| -------------- | ----------------------------------------------- |
| Admin          | All authorized administrative permissions       |
| Moderator      | Products.View, Disputes.View                    |
| DisputeOfficer | Disputes.View, Disputes.Resolve                 |
| FinanceStaff   | Transactions.View, Wallets.View                 |
| Buyer          | Auctions.View                                   |
| Seller         | Products.View, Products.Create, Products.Update |

The actual permission list must be based on existing application functionality.

---

## 4. Recommended Access Control Approach

There are two possible approaches.

### Approach A — Role-based authorization

ASP.NET Core Identity roles control access.

Example:

```csharp
[Authorize(Roles = "Admin")]
```

Or:

```csharp
[Authorize(Roles = "Admin,DisputeOfficer")]
```

Advantages:

- Simple.
- Already supported by ASP.NET Core Identity.
- Easy to maintain.
- Suitable for a small number of fixed roles.

Limitations:

- Adding a role does not automatically grant specific permissions.
- Fine-grained access control becomes difficult as the number of roles grows.
- Controllers may accumulate many role combinations.

### Approach B — Role and permission-based authorization

Roles are assigned permissions, and permissions control access to specific operations.

Example:

- Moderator receives `Disputes.View`.
- DisputeOfficer receives `Disputes.View` and `Disputes.Resolve`.
- FinanceStaff receives `Transactions.View`.

Advantages:

- More flexible.
- Roles can be configured without changing every controller.
- Multiple roles can share permissions.
- Better suited to future administrative roles.

Limitations:

- Requires additional database entities and authorization configuration.
- Requires careful permission caching and refresh handling.
- More implementation work.

### Recommendation

Use a gradual approach.

1. Preserve ASP.NET Core Identity roles.
2. Keep existing Admin, Buyer, and Seller functionality.
3. Introduce a permission system if the current requirements justify it.
4. Use ASP.NET Core authorization policies to enforce permissions.
5. Avoid building a separate authentication system.

Do not replace Identity with a custom user and role implementation.

---

## 5. Proposed Database Design

Inspect the existing Identity database tables first.

ASP.NET Core Identity already provides:

- `AspNetRoles`
- `AspNetUsers`
- `AspNetUserRoles`
- `AspNetRoleClaims`
- `AspNetUserClaims`

There may be opportunities to use the existing Identity tables rather than creating unnecessary duplicate structures.

### 5.1 Proposed Permission entity

If the existing Identity claims approach is insufficient for the required administration interface, consider adding a dedicated `Permission` entity.

| Property    | Suggested type | Purpose                      |
| ----------- | -------------- | ---------------------------- |
| Id          | int            | Primary key                  |
| Name        | string         | Unique permission identifier |
| Description | string         | Permission explanation       |
| Module      | string         | Related application module   |

Examples:

- `Products.View`
- `Products.Create`
- `Disputes.Resolve`

### 5.2 Role-Permission relationship

A role can have multiple permissions.

A permission can belong to multiple roles.

If a dedicated permission table is implemented, a `RolePermission` relationship may be required.

| Property     | Purpose                  |
| ------------ | ------------------------ |
| RoleId       | References Identity role |
| PermissionId | References Permission    |

Use appropriate foreign keys and unique constraints.

### 5.3 Important database rule

Do not duplicate existing Identity role relationships.

Inspect `AspNetRoleClaims` before deciding whether a separate `RolePermission` table is necessary.

Choose one consistent permission storage approach.

Do not implement both approaches without a specific requirement.

---

## 6. Role Management API

The administrator should be able to manage application roles.

Potential endpoints:

| Method | Endpoint                | Purpose               |
| ------ | ----------------------- | --------------------- |
| GET    | `/api/admin/roles`      | Retrieve roles        |
| GET    | `/api/admin/roles/{id}` | Retrieve role details |
| POST   | `/api/admin/roles`      | Create a role         |
| PUT    | `/api/admin/roles/{id}` | Update role details   |
| DELETE | `/api/admin/roles/{id}` | Delete a role         |

These are proposed routes. Follow existing controller naming and routing conventions.

### Role creation

An administrator can create a role such as:

`DisputeOfficer`

The backend should:

- Validate the role name.
- Prevent duplicate roles.
- Prevent reserved or invalid role names.
- Create the role using ASP.NET Core Identity.
- Return an appropriate response.

### Role modification

Allow administrators to update role metadata where appropriate.

Changing a role's name must not accidentally break existing authorization checks or frontend integrations.

### Role deletion

Before deleting a role:

- Check whether users are assigned to it.
- Check whether the role is protected.
- Prevent deletion of essential system roles.
- Ensure role-permission relationships are handled safely.

Do not allow deletion of Admin, Buyer, or Seller if existing business functionality depends on them.

---

## 7. User Role Assignment

Administrators should be able to assign and remove roles from users.

Potential endpoints:

| Method | Endpoint                               | Purpose                 |
| ------ | -------------------------------------- | ----------------------- |
| GET    | `/api/admin/users/{id}/roles`          | Retrieve a user's roles |
| PUT    | `/api/admin/users/{id}/roles`          | Update assigned roles   |
| POST   | `/api/admin/users/{id}/roles/{roleId}` | Assign a role           |
| DELETE | `/api/admin/users/{id}/roles/{roleId}` | Remove a role           |

Avoid implementing overlapping endpoints if one update endpoint can handle the required operation.

### Important validation rules

1. Only authorized administrators can assign roles.
2. The target user must exist.
3. The requested role must exist.
4. Duplicate assignments must be prevented.
5. Protected system roles must be handled carefully.
6. Prevent accidental removal of the last active administrator.
7. Validate whether a user is allowed to hold multiple roles.

### Multiple-role support

The system should consider whether a user can have multiple roles simultaneously.

For example:

A user could be both:

- Buyer
- Seller

This is a common marketplace requirement.

However, special administrative roles should have appropriate restrictions and auditing.

Do not assume that every combination of roles is valid.

---

## 8. Permission Management API

If the permission-based approach is selected, administrators should be able to view and manage role permissions.

Potential endpoints:

| Method | Endpoint                            | Purpose                        |
| ------ | ----------------------------------- | ------------------------------ |
| GET    | `/api/admin/permissions`            | Retrieve available permissions |
| GET    | `/api/admin/roles/{id}/permissions` | Retrieve role permissions      |
| PUT    | `/api/admin/roles/{id}/permissions` | Update role permissions        |

### Permission assignment

Example:

A `DisputeOfficer` role receives:

- `Disputes.View`
- `Disputes.Resolve`

A `FinanceStaff` role receives:

- `Wallets.View`
- `Transactions.View`

The backend should verify permission identifiers before saving assignments.

### Important rule

Only predefined, registered application permissions should be assignable.

Do not let administrators invent arbitrary permission strings that the application does not recognize.

---

## 9. Backend Authorization

Creating roles and assigning them to users is not sufficient.

The backend must actually enforce access restrictions.

### Role-based authorization

For existing fixed-role functionality, continue using ASP.NET Core authorization.

Example:

```csharp
[Authorize(Roles = "Admin")]
```

### Permission-based authorization

If the permission system is implemented, use authorization policies.

Conceptual example:

```csharp
[Authorize(Policy = "Disputes.Resolve")]
```

The authorization system should check whether the authenticated user has the required permission through their assigned roles.

### Important security requirements

- Authorization must be enforced on the backend.
- Frontend route protection is not sufficient.
- Do not trust role names sent by the frontend.
- Do not trust permission names sent by the frontend.
- Prevent unauthorized users from changing their own roles.
- Prevent unauthorized users from granting themselves permissions.
- Verify access to individual records, not just controller routes.
- Preserve existing ownership checks for products, auctions, sales, and disputes.

### Resource-level authorization

Some operations require more than a role or permission check.

For example:

- A seller can update only their own products.
- A buyer can access only their own purchase information.
- A DisputeOfficer can access disputes assigned to them if assignment-based access is introduced.
- FinanceStaff should not automatically receive permission to modify wallet balances.

Use the existing business logic and ownership checks.

Do not treat role authorization as a replacement for resource ownership validation.

---

## 10. Existing Controller Impact

Inspect existing controllers and determine which authorization rules should change.

Potential areas:

| Controller        | Access control consideration    |
| ----------------- | ------------------------------- |
| AuthController    | Registration and authentication |
| UserController    | User management                 |
| ProductController | Seller product ownership        |
| AuctionController | Auction management              |
| DisputeController | Dispute review and resolution   |
| WalletController  | Wallet access                   |
| SaleController    | Sale and payment operations     |
| AdminController   | Administrative operations       |

Do not modify all controllers blindly.

Create an authorization mapping based on the actual existing endpoints.

For every endpoint, document:

- Who can access it?
- What permission is required?
- Does it require record ownership?
- Does it require administrator privileges?
- Is it sensitive or financially significant?

---

## 11. System Roles vs Custom Roles

The system should distinguish between essential system roles and administrator-created roles.

### System roles

Examples:

- Admin
- Buyer
- Seller

These roles are fundamental to existing application functionality.

They should be created or verified through startup/database seeding.

They should not be accidentally deleted or renamed.

### Custom roles

Examples:

- Moderator
- DisputeOfficer
- FinanceStaff
- SupportStaff

These roles may be created and managed by authorized administrators.

Custom roles should receive only the permissions necessary for their responsibilities.

### Important recommendation

Do not make every newly created role an administrator.

A newly created role should have no elevated access until permissions are explicitly assigned.

---

## 12. Security and Audit Requirements

Role changes are sensitive administrative operations.

The system should maintain an audit history for:

- Role creation.
- Role updates.
- Role deletion.
- User role assignment.
- User role removal.
- Role permission changes.

An audit record should identify:

- Administrator who performed the operation.
- Target user or role.
- Operation performed.
- Previous state, where appropriate.
- New state, where appropriate.
- Timestamp.

Reuse an existing audit logging system if available.

Do not expose sensitive information in audit responses.

### Additional protections

- Prevent privilege escalation.
- Protect the last active administrator.
- Validate role assignment requests.
- Prevent duplicate role assignments.
- Avoid stale permission data after role changes.
- Ensure deleted or revoked permissions stop granting access.
- Log sensitive administrative operations.

---

## 13. Token and Permission Refresh

Inspect the current JWT implementation.

If role or permission claims are embedded in JWT tokens, changes to a user's roles may not immediately affect an already-issued token.

The implementation must consider this.

Possible approaches:

1. Use short-lived access tokens.
2. Refresh tokens after role changes.
3. Check current permissions through a database-backed authorization handler.
4. Use a suitable cache with reliable invalidation.

Choose the simplest approach compatible with the current authentication implementation.

Do not assume that changing a role in the database automatically changes an already-issued JWT.

---

## 14. Implementation Phases

### Batch 1 — Existing system analysis

Inspect:

- Identity configuration.
- Existing roles.
- User-role assignment.
- JWT claims.
- Authorization attributes.
- Existing admin endpoints.
- Existing migrations.
- Existing frontend role assumptions.

Deliverable:

A report describing the current authorization architecture and recommended changes.

### Batch 2 — Role management

- Implement role retrieval.
- Implement role creation.
- Implement role updates where required.
- Implement protected role deletion.
- Preserve existing system roles.

Deliverable:

Working role management APIs.

### Batch 3 — User role assignment

- Retrieve user roles.
- Assign roles.
- Remove roles.
- Support valid multiple-role combinations.
- Prevent privilege escalation.

Deliverable:

Working user role management APIs.

### Batch 4 — Permission management

Only implement this batch if the analysis confirms that role-only authorization is insufficient.

- Define the application permission catalogue.
- Select one permission storage approach.
- Implement role-permission assignment.
- Implement permission retrieval.
- Implement authorization policies.

Deliverable:

Working role-permission management.

### Batch 5 — Controller authorization

- Review existing controllers.
- Map endpoints to appropriate roles or permissions.
- Add authorization policies where necessary.
- Preserve resource ownership checks.
- Test unauthorized access.

Deliverable:

Consistent backend access control.

### Batch 6 — Audit and token handling

- Record sensitive role changes.
- Handle permission refresh.
- Verify revoked access.
- Verify JWT behavior.

Deliverable:

Secure administrative role management.

### Batch 7 — Testing

Test all scenarios below.

Deliverable:

Test results and implementation documentation.

---

## 15. Required Test Scenarios

| #   | Scenario                                         | Expected result                                   |
| --- | ------------------------------------------------ | ------------------------------------------------- |
| 1   | Admin retrieves roles                            | Success                                           |
| 2   | Admin creates a custom role                      | Success                                           |
| 3   | Duplicate role creation                          | Rejected                                          |
| 4   | Unauthorized user creates a role                 | Rejected                                          |
| 5   | Admin assigns a role to a user                   | Success                                           |
| 6   | Duplicate role assignment                        | Prevented                                         |
| 7   | Admin removes a role                             | Success                                           |
| 8   | Unauthorized user modifies another user's roles  | Rejected                                          |
| 9   | Admin attempts to delete a protected system role | Rejected                                          |
| 10  | Admin attempts to delete an assigned role        | Safely rejected or handled                        |
| 11  | User has multiple permitted roles                | Authorization behaves correctly                   |
| 12  | Custom role has no permissions                   | No elevated access                                |
| 13  | Role receives a permission                       | Authorized operation succeeds                     |
| 14  | Role loses a permission                          | Access is revoked according to token/cache policy |
| 15  | Seller accesses another seller's product         | Rejected                                          |
| 16  | Buyer accesses another buyer's purchase          | Rejected                                          |
| 17  | Non-admin attempts an administrative operation   | Rejected                                          |
| 18  | Last active administrator is removed or demoted  | Prevented                                         |
| 19  | Role changes while an old JWT exists             | Revocation behavior matches the chosen design     |
| 20  | Existing Admin, Buyer, and Seller functionality  | Continues working                                 |

---

## 16. Features Outside Initial Scope

Do not implement these unless required:

- Organization and department hierarchy.
- Multi-tenant role management.
- Dynamic permission creation by administrators.
- Complex approval workflows for role assignment.
- Attribute-based access control.
- Custom policy scripting.
- Separate authorization microservice.
- Full enterprise IAM platform.
- User-defined permission expressions.

Keep the implementation proportional to the ProductAuction application's requirements.

---

## 17. Final Acceptance Criteria

The feature is complete when:

- Existing system roles continue working.
- Administrators can create and manage appropriate custom roles.
- Administrators can assign and remove roles from users.
- Multiple-role support works according to the defined business rules.
- Role and permission checks are enforced by the backend.
- Unauthorized users cannot access protected endpoints.
- Resource ownership checks remain functional.
- Protected system roles cannot be accidentally removed.
- Role and permission changes are audited.
- Token or permission refresh behavior is handled correctly.
- Existing authentication, auction, wallet, sale, and dispute functionality remains operational.

---

## 18. Instructions to the Coding Agent

Work directly with the existing ProductAuction backend.

### First: Perform a feasibility study

Do not immediately start implementing a new RBAC system.

Report:

1. Which Identity roles currently exist.
2. How users are assigned roles.
3. Whether users can currently have multiple roles.
4. How JWT role claims are generated.
5. How existing controllers enforce authorization.
6. Whether existing Identity claims can support the required permissions.
7. Whether a dedicated Permission table is necessary.
8. Which existing endpoints require access-control changes.
9. Which database changes are necessary.
10. The simplest architecture that meets the requirements.

### Second: Recommend an implementation approach

Compare:

- Existing Identity role-based authorization.
- Identity roles combined with permissions.
- A dedicated role-permission database design.

Recommend one approach based on the existing codebase.

Do not automatically select the most complex approach.

### Third: Implement in batches

Follow the implementation phases.

Additional instructions:

- Reuse ASP.NET Core Identity.
- Preserve existing authentication.
- Do not introduce another user-management system.
- Do not duplicate existing role tables.
- Do not change existing business rules silently.
- Keep the implementation straightforward.
- Do not modify unrelated functionality.
- Provide a list of modified files.
- Explain every database migration.
- Provide testing and SSMS verification instructions.

**Primary objective: Allow ProductAuction to support additional roles and controlled access without breaking the existing Admin, Buyer, and Seller functionality or introducing unnecessary complexity.**
