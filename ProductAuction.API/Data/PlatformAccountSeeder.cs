
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models;

namespace ProductAuction.API.Data
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
                    var errors = string.Join(
                        "; ",
                        result.Errors.Select(error => error.Description));

                    throw new InvalidOperationException(
                        $"Failed to create platform account: {errors}");
                }
            }

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
            }
        }
    }
}
