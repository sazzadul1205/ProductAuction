using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.Models;

namespace ProductAuction.API.Services.Background
{
    public class AuctionResultBackgroundService : BackgroundService
    {
        // Factory used to create a new DI scope each time we access the database.
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuctionResultBackgroundService> _logger;

        public AuctionResultBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AuctionResultBackgroundService> logger)
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
                    var now = DateTime.UtcNow;

                    List<int> auctionIds;

                    // Find active auctions whose end time has passed.
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        auctionIds = await context.Auctions
                            .Where(a =>
                                a.Status == "Active" &&
                                a.EndTime.HasValue &&
                                a.EndTime.Value <= now)
                            .Select(a => a.Id)
                            .ToListAsync(stoppingToken);
                    }

                    // Process each ended auction independently 
                    foreach (var auctionId in auctionIds)
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var auction = await context.Auctions.FindAsync([auctionId], stoppingToken);
                            if (auction == null) continue;
                            await ProcessAuctionAsync(context, auction, now, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process auction #{AuctionId}.", auctionId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AuctionResultBackgroundService");
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task ProcessAuctionAsync(AppDbContext context, Auction auction, DateTime now, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Processing ended auction #{AuctionId}.", auction.Id);

            await using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);

            var existingSale = await context.Sales
                .FirstOrDefaultAsync(s => s.AuctionId == auction.Id, stoppingToken);

            string? winnerId = null;

            if (existingSale != null)
            {
                _logger.LogDebug("Auction #{AuctionId} already has a sale; reusing buyer {BuyerId}.", auction.Id, existingSale.BuyerId);
                winnerId = existingSale.BuyerId;
                auction.Status = "Completed";
            }
            else
            {
                var product = await context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId, stoppingToken);

                if (product == null)
                {
                    _logger.LogWarning("Auction #{AuctionId} skipped: product #{ProductId} not found.", auction.Id, auction.ProductId);
                    await transaction.RollbackAsync(stoppingToken);
                    return;
                }

                var highestBid = await context.Bids
                    .Where(b => b.AuctionId == auction.Id)
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.PlacedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                if (highestBid == null)
                {
                    _logger.LogInformation("Auction #{AuctionId} ended with no bids; releasing seller deposit.", auction.Id);

                    var sellerWallet = await context.Wallets.FirstOrDefaultAsync(w => w.UserId == product.SellerId, stoppingToken);

                    var sellerDeposit = await context.WalletReservations.FirstOrDefaultAsync(r =>
                    r.ProductId == auction.ProductId &&
                    r.WalletId == sellerWallet.Id &&
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved", stoppingToken);

                    bool invalidSellerState = false;

                    if (sellerWallet == null)
                    {
                        invalidSellerState = true;
                    }
                    else if (sellerDeposit != null && sellerWallet.ReservedBalance < sellerDeposit.Amount)
                    {
                        invalidSellerState = true;
                    }

                    if (invalidSellerState)
                    {
                        _logger.LogWarning("Auction #{AuctionId} skipped: seller wallet/reserved balance invalid.", auction.Id);
                        await transaction.RollbackAsync(stoppingToken);
                        return;
                    }

                    if (sellerDeposit != null)
                    {
                        sellerWallet!.ReservedBalance -= sellerDeposit.Amount;
                        sellerDeposit.Status = "Released";

                        context.WalletTransactions.Add(new WalletTransaction
                        {
                            WalletId = sellerWallet.Id,
                            Type = "Release",
                            Amount = sellerDeposit.Amount,
                            BalanceAfter = sellerWallet.Balance,
                            Reference = $"Seller deposit release for no-winner auction #{auction.Id}",
                            CreatedAt = now
                        });
                    }

                    auction.Status = "Completed";
                    auction.Result = "NoWinner";
                    auction.WinnerId = null;

                    product.Status = "Available";
                }
                else
                {
                    _logger.LogInformation("Auction #{AuctionId} sold to {BidderId} for {Amount}.", auction.Id, highestBid.BidderId, highestBid.Amount);

                    winnerId = highestBid.BidderId;

                    auction.Status = "Completed";
                    auction.Result = "Sold";
                    auction.WinnerId = highestBid.BidderId;
                    auction.CurrentHighestBid = highestBid.Amount;
                    auction.CurrentHighestBidderId = highestBid.BidderId;

                    product.Status = "Unavailable";

                    decimal commissionAmount = Math.Round(highestBid.Amount * 0.10m, 2);
                    decimal sellerProceeds = highestBid.Amount - commissionAmount;

                    context.Sales.Add(new Sale
                    {
                        AuctionId = auction.Id,
                        ProductId = auction.ProductId,
                        BuyerId = highestBid.BidderId,
                        SellerId = product.SellerId,
                        FinalAmount = highestBid.Amount,
                        CommissionAmount = commissionAmount,
                        SellerProceeds = sellerProceeds,
                        Status = "AwaitingPayment",
                        PaymentDeadline = now.AddDays(3),
                        CreatedAt = now
                    });
                }
            }

            // Find buyer reservations that are still locked for this auction.
            var reservations = await context.WalletReservations
                .Where(r =>
                    r.AuctionId == auction.Id &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
                .ToListAsync(stoppingToken);

            foreach (var reservation in reservations)
            {
                var wallet = await context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId, stoppingToken);

                if (wallet == null)
                {
                    _logger.LogWarning("Reservation #{ReservationId} skipped: wallet #{WalletId} not found.",reservation.Id, reservation.WalletId);
                    continue;
                }

                if (wallet.ReservedBalance < reservation.Amount)
                {
                    _logger.LogWarning("Reservation #{ReservationId} skipped: wallet #{WalletId} reserved balance inconsistent.",reservation.Id, reservation.WalletId);
                    continue;
                }

                if (winnerId != null && wallet.UserId == winnerId)
                    continue;

                wallet.ReservedBalance -= reservation.Amount;
                reservation.Status = "Released";
            }

            await context.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);

            _logger.LogDebug("Auction #{AuctionId} finalized. Result={Result}, Winner={WinnerId}", auction.Id, auction.Result, auction.WinnerId ?? "<none>");
        }
    }
}
