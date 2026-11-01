using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.Models;

namespace ProductAuction.API.Data.Seeder
{
    public static class AdminAccountSeeder
    {
        public static async Task SeedAdminAccountAsync(IServiceProvider serviceProvider)
        {
            // Create a scope to access Identity and Database services.
            using var scope = serviceProvider.CreateScope();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Logger resolved from the scope (static class cannot take it via ctor).
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(AdminAccountSeeder));

            // logger.LogInformation("AdminAccountSeeder started at {Time}.", DateTime.UtcNow);

            // Set the default admin account details.
            var email = "admin@gmail.com";
            var password = "Admin@123";
            var fullName = "System Administrator";

            // Start a transaction so all database changes succeed or roll back together.
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Check whether the admin account already exists.
                var adminUser = await userManager.FindByEmailAsync(email);

                if (adminUser == null)
                {
                    // Create the Admin Identity account
                    adminUser = new IdentityUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };

                    var result = await userManager.CreateAsync(adminUser, password);

                    if (!result.Succeeded)
                    {
                        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
                        throw new InvalidOperationException($"Failed to create admin account: {errors}");
                    }

                    // logger.LogInformation("Admin account {Email} created.", email);

                    // Assign the Admin role
                    var roleResult = await userManager.AddToRoleAsync(adminUser, "Admin");

                    if (!roleResult.Succeeded)
                    {
                        var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
                        throw new InvalidOperationException($"Failed to assign Admin role: {errors}");
                    }

                    // logger.LogInformation("Admin role assigned to {Email}.", email);
                }
                else
                {
                    // logger.LogDebug("Admin account {Email} already exists; verifying role.", email);

                    // If the account exists, ensure it has the Admin role
                    bool isAdmin = await userManager.IsInRoleAsync(adminUser, "Admin");

                    if (!isAdmin)
                    {
                        var roleResult = await userManager.AddToRoleAsync(adminUser, "Admin");

                        if (!roleResult.Succeeded)
                        {
                            var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
                            throw new InvalidOperationException($"Failed to assign Admin role to existing account: {errors}");
                        }

                        // logger.LogInformation("Admin role added to existing account {Email}.", email);
                    }
                }

                // 2. Ensure the UserProfile exists
                var profileExists = await context.UserProfiles.AnyAsync(p => p.UserId == adminUser.Id);

                if (!profileExists)
                {
                    var profile = new UserProfile
                    {
                        UserId = adminUser.Id,
                        FullName = fullName,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.UserProfiles.Add(profile);

                    // logger.LogInformation("UserProfile created for admin {Email}.", email);
                }

                // 3. Ensure the Wallet exists
                var walletExists = await context.Wallets.AnyAsync(w => w.UserId == adminUser.Id);

                if (!walletExists)
                {
                    var wallet = new Wallet
                    {
                        UserId = adminUser.Id,
                        Balance = 0,
                        ReservedBalance = 0
                    };

                    context.Wallets.Add(wallet);

                    // logger.LogInformation("Wallet created for admin {Email}.", email);
                }

                // Save profile and wallet changes to the database.
                await context.SaveChangesAsync();

                // System.Diagnostics.Debug.Assert(await context.UserProfiles.AnyAsync(p => p.UserId == adminUser.Id));
                // System.Diagnostics.Debug.Assert(await context.Wallets.AnyAsync(w => w.UserId == adminUser.Id));
                // System.Diagnostics.Debug.Assert(await userManager.IsInRoleAsync(adminUser, "Admin"));

                // Commit the transaction only after everything succeeds.
                await transaction.CommitAsync();

                // logger.LogInformation("AdminAccountSeeder completed successfully for {Email}.", email);
            }
            catch (Exception ex)
            {
                // logger.LogError(ex, "AdminAccountSeeder failed for {Email}; rolling back.", email);

                // Undo the database changes if an unexpected error occurs.
                await transaction.RollbackAsync();
                throw;
            }

            // logger.LogInformation("AdminAccountSeeder finished at {Time}.", DateTime.UtcNow);
        }
    }
}