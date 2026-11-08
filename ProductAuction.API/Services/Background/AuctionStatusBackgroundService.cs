
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Services.Background
{
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Keep checking until the application is stopping.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
                        _logger.LogInformation("Auction #{AuctionId} activated.", auction.Id);
                    }

                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error In AuctionStatusBackgroundService");
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
