using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Services.Background
{
    // Background service that identifies deliveries not dispatched on time.
    public class DispatchOverdueBackgroundService : BackgroundService
    {
        // Factory used to create a new DI scope each time we access the database.
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DispatchOverdueBackgroundService> _logger;

        public DispatchOverdueBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<DispatchOverdueBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Main loop that runs until the application shuts down.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // _logger.LogInformation("DispatchOverdueBackgroundService started at {Time}.", DateTime.UtcNow);

            // Keep checking until the application is stopping.
            while (!stoppingToken.IsCancellationRequested)
            {
                // Create a scope because AppDbContext is scoped.
                using (var scope = _scopeFactory.CreateScope())
                {
                    // Get the database context.
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Current UTC time.
                    var now = DateTime.UtcNow;

                    // Find deliveries whose dispatch deadline has passed
                    // and that have not been dispatched.
                    var overdueDeliveries = await context.DeliveryRecords
                        .Where(d =>
                            d.Status == "AwaitingDispatch" &&
                            d.DispatchDeadline <= now)
                        .ToListAsync(stoppingToken);

                    // Mark each missed dispatch deadline.
                    foreach (var delivery in overdueDeliveries)
                    {
                        delivery.Status = "DispatchOverdue";
                        // _logger.LogWarning("Delivery #{DeliveryId} marked DispatchOverdue at {Time}.", delivery.Id, now);
                    }

                    // Save changes only when there are overdue deliveries.
                    if (overdueDeliveries.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);

                        // System.Diagnostics.Debug.Assert(overdueDeliveries.All(d => d.Status == "DispatchOverdue"));
                    }
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            // _logger.LogInformation("DispatchOverdueBackgroundService stopped at {Time}.", DateTime.UtcNow);
        }
    }
}