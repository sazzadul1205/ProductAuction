using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Services.Background
{
    // Background service that marks unpaid sales as overdue.
    public class PaymentOverdueBackgroundService : BackgroundService
    {
        // Factory used to create a new DI scope each time we access the database.
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PaymentOverdueBackgroundService> _logger;

        public PaymentOverdueBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<PaymentOverdueBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Main loop that runs until the application shuts down.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // _logger.LogInformation("PaymentOverdueBackgroundService started at {Time}.", DateTime.UtcNow);

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

                    // Find sales whose payment deadline has passed
                    // and whose payment has not been completed.
                    var overdueSales = await context.Sales
                        .Where(s =>
                            s.Status == "AwaitingPayment" &&
                            s.PaymentDeadline <= now)
                        .ToListAsync(stoppingToken);

                    // Mark each unpaid sale as overdue.
                    foreach (var sale in overdueSales)
                    {
                        sale.Status = "PaymentOverdue";
                        // _logger.LogWarning("Sale #{SaleId} marked PaymentOverdue at {Time}.", sale.Id, now);
                    }

                    // Save changes only when there are overdue sales.
                    if (overdueSales.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);

                        // System.Diagnostics.Debug.Assert(overdueSales.All(s => s.Status == "PaymentOverdue"));
                    }
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            // _logger.LogInformation("PaymentOverdueBackgroundService stopped at {Time}.", DateTime.UtcNow);
        }
    }
}