using Microsoft.AspNetCore.Identity;

namespace ProductAuction.API.Data.Seeder
{
    public static class RoleSeeder
    {
        // Roles that should exist in the database.
        private static readonly string[] Roles =
        [
            "Admin",
            "Seller",
            "Buyer"
        ];

        public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
        {
            // Create a scope to access Identity services.
            using var scope = serviceProvider.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Logger resolved from the scope (static class cannot take it via ctor).
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(RoleSeeder));

            // logger.LogInformation("RoleSeeder started at {Time}.", DateTime.UtcNow);

            // Check each role and create it if it does not exist.
            foreach (var role in Roles)
            {
                bool roleExists = await roleManager.RoleExistsAsync(role);

                if (!roleExists)
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(role));

                    if (!result.Succeeded)
                    {
                        var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"Failed to create role '{role}': {errors}");
                    }

                    // logger.LogInformation("Role '{Role}' created.", role);

                    // System.Diagnostics.Debug.Assert(await roleManager.RoleExistsAsync(role));
                }
                // else
                // {
                //     logger.LogDebug("Role '{Role}' already exists; skipping.", role);
                // }
            }

            // logger.LogInformation("RoleSeeder completed at {Time}.", DateTime.UtcNow);
        }
    }
}