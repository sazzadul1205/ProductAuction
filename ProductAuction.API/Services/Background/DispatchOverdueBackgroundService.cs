
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Services.Background
{
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

                    // Find deliveries whose dispatch deadline has passed
                    var overdueDeliveries = await context.DeliveryRecords
                        .Where(d => d.Status == "AwaitingDispatch" && d.DispatchDeadline <= now)
                        .ToListAsync(stoppingToken);

                    // Mark each missed dispatch deadline.
                    foreach (var delivery in overdueDeliveries)
                    {
                        delivery.Status = "DispatchOverdue";
                        _logger.LogWarning("Delivery #{DeliveryId} marked as DispatchOverdue.", delivery.Id);
                    }

                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error In DispatchOverdueBackgroundService");
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
