using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Services.Background
{
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;

                    // Find sales whose payment deadline has passed.
                    var overdueSales = await context.Sales
                        .Where(s => s.Status == SaleStatus.AwaitingPayment && s.PaymentDeadline <= now)
                        .ToListAsync(stoppingToken);

                    // Change each unpaid sale as overdue.
                    foreach (var sale in overdueSales)
                    {
                        sale.Status = SaleStatus.PaymentOverdue;
                        _logger.LogWarning("Sale {SaleId} marked as PaymentOverdue.", sale.Id);
                    }

                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error In PaymentOverdueBackgroundService");
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}