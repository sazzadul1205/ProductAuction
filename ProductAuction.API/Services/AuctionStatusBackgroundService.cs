using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Services
{
    // Background service that runs continuously and keeps auction statuses up to date.
    public class AuctionStatusBackgroundService : BackgroundService
    {
        // Factory used to create a new DI scope each time we touch the database.
        private readonly IServiceScopeFactory _scopeFactory;

        public AuctionStatusBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        // Main loop of the background service. Runs until the app shuts down.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Keep repeating until the application is stopping.
            while (!stoppingToken.IsCancellationRequested)
            {
                // A background service is a singleton, so we cannot inject
                // AppDbContext directly (it is scoped). Create a fresh scope instead.
                using (var scope = _scopeFactory.CreateScope())
                {
                    // Get the database context from this scope.
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Current UTC time, used to compare against auction start/end times.
                    var now = DateTime.UtcNow;

                    // Find scheduled auctions whose start time has arrived.
                    // Conditions:
                    //   - Status is still "Upcoming"
                    //   - StartTime and EndTime are both set
                    //   - StartTime has already passed
                    //   - EndTime has not passed yet
                    var auctions = await context.Auctions
                        .Where(a =>
                            a.Status == "Upcoming" &&
                            a.StartTime.HasValue &&
                            a.EndTime.HasValue &&
                            a.StartTime.Value <= now &&
                            a.EndTime.Value > now)
                        .ToListAsync(stoppingToken);

                    // Flip each matching auction to "Active".
                    foreach (var auction in auctions)
                    {
                        auction.Status = "Active";
                    }

                    // Save only if we actually changed something.
                    if (auctions.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}