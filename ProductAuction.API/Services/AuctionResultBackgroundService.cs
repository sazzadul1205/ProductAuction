
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.Models;

namespace ProductAuction.API.Services
{
    // Background service that processes auctions after they end.
    public class AuctionResultBackgroundService : BackgroundService
    {
        // Factory used to create a new DI scope each time we access the database.
        private readonly IServiceScopeFactory _scopeFactory;

        public AuctionResultBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        // Main loop that runs until the application shuts down.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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

                    // Find active auctions whose end time has passed.
                    var auctions = await context.Auctions
                        .Where(a =>
                            a.Status == "Active" &&
                            a.EndTime.HasValue &&
                            a.EndTime.Value <= now)
                        .ToListAsync(stoppingToken);

                    // Process each ended auction.
                    foreach (var auction in auctions)
                    {
                        // Start a transaction for this auction.
                        await using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);

                        // Check whether a sale already exists.
                        var existingSale = await context.Sales
                            .FirstOrDefaultAsync(
                                s => s.AuctionId == auction.Id,
                                stoppingToken);

                        // Store the winner's user ID.
                        string? winnerId = null;

                        // If a sale already exists, use its buyer as the winner.
                        if (existingSale != null)
                        {
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

                            // If nobody placed a bid, complete without a winner.
                            if (highestBid == null)
                            {
                                auction.Status = "Completed";
                                auction.Result = "NoWinner";
                                auction.WinnerId = null;
                            }
                            else
                            {
                                // Find the product to identify its seller.
                                var product = await context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId, stoppingToken);

                                // If the product is missing, try again next time.
                                if (product == null)
                                {
                                    await transaction.RollbackAsync(stoppingToken);
                                    continue;
                                }

                                // Store the winner's ID.
                                winnerId = highestBid.BidderId;

                                // Update the auction with the winning bid.
                                auction.Status = "Completed";
                                auction.Result = "Sold";
                                auction.WinnerId = highestBid.BidderId;
                                auction.CurrentHighestBid = highestBid.Amount;
                                auction.CurrentHighestBidderId = highestBid.BidderId;

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
                    }
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
