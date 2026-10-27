using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

// TODO: Handle overdue auctions whose StartTime and EndTime have both passed
// while the application or background service was stopped.
// Consider activating overdue auctions even if EndTime has passed, so that

// AuctionResultBackgroundService can process them and mark them Completed.
namespace ProductAuction.API.Services.Background
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
                using (var scope = _scopeFactory.CreateScope())
                {
                    // Get the database context from this scope.
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Current UTC time, used to compare against auction start/end times.
                    var now = DateTime.UtcNow;

                    // Find scheduled auctions whose start time has arrived.
                    var auctions = await context.Auctions
                        .Where(a =>
                            a.Status == "Upcoming" &&
                            a.StartTime.HasValue &&
                            a.StartTime.Value <= now)
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