using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.Models;

namespace ProductAuction.API.Services.Background
{
    // Background service that processes auctions after they end.
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

        // Main loop that runs until the application shuts down.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AuctionResultBackgroundService started at {Time}.", DateTime.UtcNow);

            // Keep checking until the application is stopping.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Create a scope because AppDbContext is scoped.
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        // Get the database context.
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        // Current UTC time.
                        var now = DateTime.UtcNow;

                        // Find active auctions whose end time has passed.
                        var auctions = await context.Auctions
                            .Where(a =>
                                a.Status == "Active" &&
                                a.EndTime.HasValue &&
                                a.EndTime.Value <= now)
                            .ToListAsync(stoppingToken);

                        if (auctions.Count > 0)
                        {
                            _logger.LogInformation("Found {Count} ended auction(s) to process.", auctions.Count);
                        }

                        // Process each ended auction independently — one failure
                        // must not block the others.
                        foreach (var auction in auctions)
                        {
                            try
                            {
                                await ProcessAuctionAsync(context, auction, now, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to process auction #{AuctionId}.", auction.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in auction result processing cycle.");
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            _logger.LogInformation("AuctionResultBackgroundService stopped at {Time}.", DateTime.UtcNow);
        }

        // Process a single ended auction: determine winner, create sale,
        // release reservations, and mark the auction as Completed.
        private async Task ProcessAuctionAsync(AppDbContext context, Auction auction, DateTime now, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Processing ended auction #{AuctionId}.", auction.Id);

            // Start a transaction for this auction.
            await using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);

            // Check whether a sale already exists.
            var existingSale = await context.Sales
                .FirstOrDefaultAsync(s => s.AuctionId == auction.Id, stoppingToken);

            // Store the winner's user ID.
            string? winnerId = null;

            // If a sale already exists, use its buyer as the winner.
            if (existingSale != null)
            {
                _logger.LogDebug("Auction #{AuctionId} already has a sale; reusing buyer {BuyerId}.", auction.Id, existingSale.BuyerId);
                winnerId = existingSale.BuyerId;
                auction.Status = "Completed";
            }
            else
            {
                // Find the highest bid.
                // If amounts are equal, the earlier bid takes priority.
                var highestBid = await context.Bids
                    .Where(b => b.AuctionId == auction.Id)
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.PlacedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                // If nobody placed a bid, complete the auction
                // without a winner and release the seller's deposit.
                if (highestBid == null)
                {
                    _logger.LogInformation("Auction #{AuctionId} ended with no bids; releasing seller deposit.", auction.Id);

                    // Find the product to identify its seller.
                    var product = await context.Products
                        .FirstOrDefaultAsync(p => p.Id == auction.ProductId, stoppingToken);

                    // If the product is missing, roll back and skip.
                    if (product == null)
                    {
                        _logger.LogWarning("Auction #{AuctionId} skipped: product #{ProductId} not found.", auction.Id, auction.ProductId);
                        await transaction.RollbackAsync(stoppingToken);
                        return;
                    }

                    // Find the seller's wallet.
                    var sellerWallet = await context.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == product.SellerId, stoppingToken);

                    // Find the reserved deposit belonging to this
                    // seller and product, not another user's wallet.
                    var sellerDeposit = sellerWallet == null
                        ? null
                        : await context.WalletReservations
                            .FirstOrDefaultAsync(r =>
                                r.ProductId == auction.ProductId &&
                                r.WalletId == sellerWallet.Id &&
                                r.Type == "SellerDeposit" &&
                                r.Status == "Reserved", stoppingToken);

                    // Do not complete the auction if the seller's
                    // wallet is missing or its reserved balance
                    // cannot cover the deposit being released.
                    if (sellerWallet == null ||
                        (sellerDeposit != null && sellerWallet.ReservedBalance < sellerDeposit.Amount))
                    {
                        _logger.LogWarning("Auction #{AuctionId} skipped: seller wallet/reserved balance invalid.", auction.Id);
                        await transaction.RollbackAsync(stoppingToken);
                        return;
                    }

                    // Release the seller's deposit, if one exists.
                    if (sellerDeposit != null)
                    {
                        sellerWallet.ReservedBalance -= sellerDeposit.Amount;
                        sellerDeposit.Status = "Released";

                        // Record the release in the wallet history.
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

                    // Mark the auction as completed without a winner.
                    auction.Status = "Completed";
                    auction.Result = "NoWinner";
                    auction.WinnerId = null;
                }
                else
                {
                    _logger.LogInformation("Auction #{AuctionId} sold to {BidderId} for {Amount}.", auction.Id, highestBid.BidderId, highestBid.Amount);

                    // Find the product to identify its seller.
                    var product = await context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId, stoppingToken);

                    // If the product is missing, skip this auction.
                    if (product == null)
                    {
                        _logger.LogWarning("Auction #{AuctionId} skipped: product #{ProductId} not found.", auction.Id, auction.ProductId);
                        await transaction.RollbackAsync(stoppingToken);
                        return;
                    }

                    // Store the winner's ID.
                    winnerId = highestBid.BidderId;

                    // Update the auction with the winning bid.
                    auction.Status = "Completed";
                    auction.Result = "Sold";
                    auction.WinnerId = highestBid.BidderId;
                    auction.CurrentHighestBid = highestBid.Amount;
                    auction.CurrentHighestBidderId = highestBid.BidderId;

                    // Mark product as unavailable after sale
                    product.Status = "Unavailable";

                    // Calculate the platform commission (10%).
                    decimal commissionAmount = Math.Round(highestBid.Amount * 0.10m, 2);

                    // Calculate the seller's proceeds.
                    decimal sellerProceeds = highestBid.Amount - commissionAmount;

                    // Create a sale record for the winning bidder.
                    var sale = new Sale
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
                    };

                    context.Sales.Add(sale);
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
                // Find the wallet linked to this reservation.
                var wallet = await context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId, stoppingToken);

                // Skip if the wallet no longer exists.
                if (wallet == null)
                {
                    _logger.LogWarning("Reservation #{ReservationId} skipped: wallet #{WalletId} not found.", reservation.Id, reservation.WalletId);
                    continue;
                }

                // Keep the winner's reservation locked.
                if (winnerId != null && wallet.UserId == winnerId)
                {
                    continue;
                }

                // Release the non-winner's reserved funds.
                wallet.ReservedBalance -= reservation.Amount;
                reservation.Status = "Released";
            }

            // Save the auction result, sale, and reservation changes together.
            await context.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);

            _logger.LogDebug("Auction #{AuctionId} finalized. Result={Result}, Winner={WinnerId}",
                auction.Id, auction.Result, auction.WinnerId ?? "<none>");
        }
    }
}
