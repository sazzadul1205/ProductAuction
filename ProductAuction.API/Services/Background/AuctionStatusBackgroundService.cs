using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

// AuctionResultBackgroundService can process them and mark them Completed.
namespace ProductAuction.API.Services.Background
{
    // Background service that runs continuously and keeps auction statuses up to date.
    public class AuctionStatusBackgroundService : BackgroundService
    {
        // Factory used to create a new DI scope each time we touch the database.
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuctionStatusBackgroundService> _logger;

        public AuctionStatusBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AuctionStatusBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Main loop of the background service. Runs until the app shuts down.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // _logger.LogInformation("AuctionStatusBackgroundService started at {Time}.", DateTime.UtcNow);

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
                        // _logger.LogInformation("Auction #{AuctionId} activated at {Time}.", auction.Id, now);
                    }

                    // Save only if we actually changed something.
                    if (auctions.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);

                        // System.Diagnostics.Debug.Assert(auctions.All(a => a.Status == "Active"));
                    }
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            // _logger.LogInformation("AuctionStatusBackgroundService stopped at {Time}.", DateTime.UtcNow);
        }
    }
}