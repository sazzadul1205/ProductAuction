using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Services.Background
{
    public class RefreshTokenCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RefreshTokenCleanupBackgroundService> _logger;

        public RefreshTokenCleanupBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RefreshTokenCleanupBackgroundService> logger)
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

                    // Mark expired tokens
                    var expiredToken = await context.RefreshTokens
                        .Where(rt => rt.IsActive && rt.ExpiresAt <= now)
                        .ToListAsync(stoppingToken);

                    foreach (var token in expiredToken)
                    {
                        token.IsExpired = true;
                        token.IsActive = false;
                    }

                    // Mark revoked tokens as inactive
                    var revoked = await context.RefreshTokens
                        .Where(rt => rt.IsRevoked && rt.IsActive)
                        .ToListAsync(stoppingToken);

                    foreach (var t in revoked)
                    {
                        t.IsActive = false;
                    }


                    await context.SaveChangesAsync(stoppingToken);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RefreshTokenCleanupBackgroundService");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}