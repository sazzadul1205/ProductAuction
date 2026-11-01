using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models;

namespace ProductAuction.API.Data.Seeder
{
    public static class PlatformAccountSeeder
    {
        // Reserved username for the platform's system account.
        private const string PlatformUserName = "platform-system-account";

        public static async Task SeedPlatformAccountAsync(IServiceProvider serviceProvider)
        {
            // Create a scope to access Identity and database services.
            using var scope = serviceProvider.CreateScope();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Logger resolved from the scope (static class cannot take it via ctor).
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(PlatformAccountSeeder));

            // logger.LogInformation("PlatformAccountSeeder started at {Time}.", DateTime.UtcNow);

            // Check whether the platform account already exists.
            var platformUser = await userManager.FindByNameAsync(PlatformUserName);

            // Create the account if it does not exist.
            if (platformUser == null)
            {
                platformUser = new IdentityUser
                {
                    UserName = PlatformUserName,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(platformUser);

                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(error => error.Description));

                    throw new InvalidOperationException($"Failed to create platform account: {errors}");
                }

                // logger.LogInformation("Platform account '{User}' created.", PlatformUserName);
            }
            // else
            // {
            //     logger.LogDebug("Platform account '{User}' already exists; skipping creation.", PlatformUserName);
            // }

            // Check whether the platform wallet already exists.
            bool walletExists = await context.Wallets.AnyAsync(wallet => wallet.UserId == platformUser.Id);

            // Create the wallet only if it does not exist.
            if (!walletExists)
            {
                var platformWallet = new Wallet
                {
                    UserId = platformUser.Id,
                    Balance = 0m,
                    ReservedBalance = 0m
                };

                context.Wallets.Add(platformWallet);

                await context.SaveChangesAsync();

                // logger.LogInformation("Platform wallet created for user {UserId}.", platformUser.Id);

                // System.Diagnostics.Debug.Assert(await context.Wallets.AnyAsync(w => w.UserId == platformUser.Id));
            }

            // logger.LogInformation("PlatformAccountSeeder completed at {Time}.", DateTime.UtcNow);
        }
    }
}