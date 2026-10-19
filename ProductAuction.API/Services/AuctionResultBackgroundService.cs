
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
                        // Check whether a sale already exists for this auction.
                        var saleExists = await context.Sales.AnyAsync(s => s.AuctionId == auction.Id, stoppingToken);

                        // Skip if a sale has already been created.
                        if (saleExists)
                        {
                            auction.Status = "Completed";
                            continue;
                        }

                        // Find the highest bid for this auction.
                        // If amounts are equal, the earlier bid takes priority.
                        var highestBid = await context.Bids
                            .Where(b => b.AuctionId == auction.Id)
                            .OrderByDescending(b => b.Amount)
                            .ThenBy(b => b.PlacedAt)
                            .FirstOrDefaultAsync(stoppingToken);

                        // If no bids exist, complete the auction without a winner.
                        if (highestBid == null)
                        {
                            auction.Status = "Completed";
                            auction.Result = "NoWinner";
                            auction.WinnerId = null;

                            continue;
                        }

                        // Find the product to identify its seller.
                        var product = await context.Products
                            .FirstOrDefaultAsync(
                                p => p.Id == auction.ProductId,
                                stoppingToken);

                        // Skip this auction if its product cannot be found.
                        // It will be checked again during the next iteration.
                        if (product == null)
                        {
                            continue;
                        }

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

                    // Save all auction results and new sale records.
                    if (auctions.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }

                // Wait 30 seconds before checking again.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
