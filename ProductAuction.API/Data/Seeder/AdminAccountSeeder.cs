
using Microsoft.AspNetCore.Identity;

namespace ProductAuction.API.Data.Seeder
{
    public static class AdminAccountSeeder
    {
        public static async Task SeedAdminAccountAsync(IServiceProvider serviceProvider)
        {
            // Create a scope to access Identity services.
            using var scope = serviceProvider.CreateScope();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // Set the default admin account details.
            var email = "admin@gmail.com";
            var password = "Admin@123";

            // Check whether the admin account already exists.
            var adminUser = await userManager.FindByEmailAsync(email);

            // Create the admin account if it does not exist.
            if (adminUser == null)
            {
                adminUser = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(adminUser,password);

                if (!result.Succeeded)
                {
                    var errors = string.Join("; ",result.Errors.Select(error => error.Description));

                    throw new InvalidOperationException($"Failed to create admin account: {errors}");
                }
            }

            // Assign the Admin role if the account does not already have it.
            bool isAdmin = await userManager.IsInRoleAsync(adminUser,"Admin");

            if (!isAdmin)
            {
                var result = await userManager.AddToRoleAsync(adminUser,"Admin");

                if (!result.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        result.Errors.Select(error => error.Description));

                    throw new InvalidOperationException($"Failed to assign Admin role: {errors}");
                }
            }
        }
    }
}
