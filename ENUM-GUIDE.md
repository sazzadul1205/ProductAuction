# Enum Implementation Guide (Backend)

## Goal
Replace magic strings (e.g., `"Draft"`, `"Approved"`, `"Active"`) with C# enums for type safety, while keeping existing database values compatible.

## Approach (EF Core string conversion)
1. **Define enums** (in `Models/Enums/` or alongside models):
   ```csharp
   public enum ProductStatus { Draft, Available, Unavailable }
   public enum ApprovalStatus { PendingApproval, Approved, Rejected }
   public enum AuctionStatus { Upcoming, Active, Completed, Cancelled }
   public enum AuctionResult { None, Sold, NoWinner }
   ```
2. **Use on entities**:
   ```csharp
   public ProductStatus Status { get; set; } = ProductStatus.Draft;
   public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.PendingApproval;
   ```
   (Or keep `string` properties and convert in code; entity-typed is preferred.)
3. **Configure EF Core** (`AppDbContext.OnModelCreating`):
   ```csharp
   modelBuilder.Entity<Product>()
       .Property(p => p.Status).HasConversion<string>();
   modelBuilder.Entity<Product>()
       .Property(p => p.ApprovalStatus).HasConversion<string>();
   modelBuilder.Entity<Auction>()
       .Property(a => a.Status).HasConversion<string>();
   modelBuilder.Entity<Auction>()
       .Property(a => a.Result).HasConversion<string?>();
   ```
4. **Update usages**: replace string comparisons/assignments (`"Draft"`) with `ProductStatus.Draft`. JSON serialization defaults to string names with `System.Text.Json` when configured (`JsonStringEnumConverter`) if you want API payloads unchanged.
5. **Migration**: string conversion typically doesn’t change the column type; verify with `dotnet ef migrations add UseEnums` and inspect the migration. Apply only if schema changes are intended.
6. **DTOs/API**: keep request/response contracts stable (string names) by adding `[JsonConverter(typeof(JsonStringEnumConverter))]` or global converter in `Program.cs`.

## Notes
- Non-trivial refactor across controllers, services, and background jobs.
- Do incrementally per entity; run tests after each.
- Preserve existing DB values (enum member names must match stored strings).