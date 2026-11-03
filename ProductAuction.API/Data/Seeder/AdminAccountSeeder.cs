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

            // Start a transaction 
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Check whether the admin account already exists.
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

                // Ensure the UserProfile exists
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

                // Save profile changes to the database.
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // logger.LogInformation("AdminAccountSeeder completed successfully for {Email}.", email);
            }
            catch (Exception ex)
            {
                // logger.LogError(ex, "AdminAccountSeeder failed for {Email}; rolling back.", email);

                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}