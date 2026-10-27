
using Microsoft.AspNetCore.Identity;

namespace ProductAuction.API.Data.Seeder
{
    public static class RoleSeeder
    {
        // Roles that should exist in the database.
        private static readonly string[] Roles =
        {
            "Admin",
            "Seller",
            "Buyer"
        };

        public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
        {
            // Create a scope to access Identity services.
            using var scope = serviceProvider.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Check each role and create it if it does not exist.
            foreach (var role in Roles)
            {
                bool roleExists = await roleManager.RoleExistsAsync(role);

                if (!roleExists)
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }
    }
}
