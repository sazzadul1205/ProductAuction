using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Bid;
using ProductAuction.API.Models;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BidController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BidController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/bid
        // Place a bid on an active auction.
        [HttpPost]
        [Authorize(Roles = "Buyer")]
        public async Task<IActionResult> PlaceBid(PlaceBidDto dto)
        {
            // Read the logged-in user's ID from the JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // Find the auction.
            var auction = await _context.Auctions
                .FirstOrDefaultAsync(a => a.Id == dto.AuctionId);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // Find the product associated with this auction.
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == auction.ProductId);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Bids can only be placed on active auctions.
            if (auction.Status != "Active")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Bids can only be placed on active auctions."
                });
            }

            // Make sure the auction has not passed its end time.
            if (auction.EndTime == null || auction.EndTime <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This auction has ended."
                });
            }

            // The product must be approved and available before anyone can bid.
            if (product.ApprovalStatus != "Approved" || product.Status != "Available")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved or available for bidding."
                });
            }

            // Sellers cannot bid on their own products.
            if (auction.SellerId == userId)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "You cannot bid on your own product."
                });
            }

            // Bids at or above the buyout price must use the buyout endpoint.
            if (product.BuyoutPrice > 0 && dto.Amount >= product.BuyoutPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Your bid must be below the buyout price. Use the buyout endpoint to purchase immediately."
                });
            }

            // The first bid must meet or exceed the starting price.
            if (dto.Amount < product.StartingPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Your bid must meet or exceed the starting price."
                });
            }

            // Each new bid must exceed the current highest bid.
            if (auction.CurrentHighestBid.HasValue && dto.Amount <= auction.CurrentHighestBid.Value)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Your bid must exceed the current highest bid."
                });
            }

            // TODO: Implement the 3-tier bidding system.
            // Clarify tier boundaries and minimum bid increments with the team.
            // For now, only require each new bid to exceed the current highest bid.

            // Find the buyer's wallet.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet not found."
                });
            }

            // Check whether this buyer already has a reservation for this auction.
            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.AuctionId == auction.Id &&
                    r.WalletId == wallet.Id &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved");

            // Reserve 25% of the starting price once per auction.
            if (reservation == null)
            {
                var requiredAmount = product.StartingPrice * 0.25m;

                // Reserved funds cannot be reused.
                var availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "You need at least 25% of the starting price available in your wallet to participate."
                    });
                }

                // Add the required amount to the reserved balance.
                wallet.ReservedBalance += requiredAmount;

                // Record the buyer's reservation.
                reservation = new WalletReservation
                {
                    WalletId = wallet.Id,
                    ProductId = auction.ProductId,
                    AuctionId = auction.Id,
                    Type = "BuyerFunds",
                    Amount = requiredAmount,
                    Status = "Reserved"
                };

                _context.WalletReservations.Add(reservation);
            }

            // Create the bid record.
            var bid = new Bid
            {
                AuctionId = auction.Id,
                BidderId = userId,
                Amount = dto.Amount,
                PlacedAt = DateTime.UtcNow
            };

            _context.Bids.Add(bid);

            // Update the auction's highest-bid information.
            auction.CurrentHighestBid = dto.Amount;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Bid placed successfully.",
                data = new
                {
                    bid.Id,
                    bid.AuctionId,
                    bid.Amount,
                    bid.PlacedAt,
                    auction.CurrentHighestBid,
                    auction.BidCount
                }
            });
        }

        // GET: api/bid/auction/5/bids
        // Gets the bid history for a specific auction.
        [HttpGet("auction/{auctionId}/bids")]
        public async Task<IActionResult> GetAuctionBidHistory(int auctionId)
        {
            // Check whether the auction exists.
            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // Retrieve bids for this auction, newest first.
            var bids = await _context.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.PlacedAt)
                .Select(b => new BidHistoryDto
                {
                    BidderId = b.BidderId,
                    Amount = b.Amount,
                    PlacedAt = b.PlacedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Bids for this Auction Returned Successfully",
                data = bids
            });
        }

        // POST: api/bid/5/buyout
        // Allows a buyer to purchase a product immediately at its buyout price.
        [HttpPost("{auctionId}/buyout")]
        [Authorize(Roles = "Buyer")]
        public async Task<IActionResult> Buyout(int auctionId)
        {
            // Get the logged-in user's ID.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // Find the auction.
            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // The auction must currently be active.
            if (auction.Status != "Active")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This auction is not active."
                });
            }

            // The auction must not have passed its end time.
            if (!auction.EndTime.HasValue || auction.EndTime.Value <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This auction has ended."
                });
            }

            // Find the product being auctioned.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Prevent sellers from buying their own products.
            if (auction.SellerId == userId)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "You cannot buy out your own product."
                });
            }

            // Only approved and available products can be purchased.
            if (product.ApprovalStatus != "Approved" || product.Status != "Available")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved or available for purchase."
                });
            }

            // A valid buyout price must be configured.
            if (product.BuyoutPrice <= 0)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product has no valid buyout price."
                });
            }

            // Prevent creating another sale for the same auction.
            var existingSale = await _context.Sales.AnyAsync(s => s.AuctionId == auctionId);

            if (existingSale)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A sale already exists for this auction."
                });
            }

            // Start a transaction so all changes succeed or fail together.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Find the buyer's wallet.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet not found."
                });
            }

            // Check whether the buyer already has a reservation for this auction.
            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.WalletId == wallet.Id &&
                    r.AuctionId == auctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved");

            // If the buyer has not participated before, reserve 25% of the starting price.
            if (reservation == null)
            {
                decimal requiredAmount = product.StartingPrice * 0.25m;

                decimal availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "You do not have enough available balance to participate."
                    });
                }

                // Reserve the required amount.
                wallet.ReservedBalance += requiredAmount;

                // Record the reservation.
                reservation = new WalletReservation
                {
                    WalletId = wallet.Id,
                    ProductId = product.Id,
                    AuctionId = auction.Id,
                    Type = "BuyerFunds",
                    Amount = requiredAmount,
                    Status = "Reserved"
                };

                _context.WalletReservations.Add(reservation);
            }

            // Record the buyout as a bid.
            var buyoutBid = new Bid
            {
                AuctionId = auction.Id,
                BidderId = userId,
                Amount = product.BuyoutPrice,
                IsBuyout = true,
                PlacedAt = DateTime.UtcNow
            };

            _context.Bids.Add(buyoutBid);

            // Complete the auction and assign the buyer as winner.
            auction.Status = "Completed";
            auction.Result = "Sold";
            auction.WinnerId = userId;
            auction.CurrentHighestBid = product.BuyoutPrice;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            // Calculate the platform commission (10%).
            decimal commissionAmount = Math.Round(product.BuyoutPrice * 0.10m, 2);

            // Calculate the seller's proceeds.
            decimal sellerProceeds = product.BuyoutPrice - commissionAmount;

            // Create the sale record.
            var now = DateTime.UtcNow;

            var sale = new Sale
            {
                AuctionId = auction.Id,
                ProductId = product.Id,
                BuyerId = userId,
                SellerId = product.SellerId,
                FinalAmount = product.BuyoutPrice,
                CommissionAmount = commissionAmount,
                SellerProceeds = sellerProceeds,
                Status = "AwaitingPayment",
                PaymentDeadline = now.AddDays(3),
                CreatedAt = now
            };

            _context.Sales.Add(sale);

            // Find all buyer reservations still locked for this auction.
            var reservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == auction.Id &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
                .ToListAsync();

            foreach (var buyerReservation in reservations)
            {
                // Keep the buyout winner's reservation locked.
                if (buyerReservation.WalletId == wallet.Id)
                {
                    continue;
                }

                // Find the non-winner's wallet.
                var otherWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == buyerReservation.WalletId);

                // Skip if the wallet no longer exists.
                if (otherWallet == null)
                {
                    continue;
                }

                // Release the non-winner's reserved funds.
                otherWallet.ReservedBalance -= buyerReservation.Amount;
                buyerReservation.Status = "Released";
            }

            // Save the bid, auction, sale, and reservation changes together.
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = "Buyout successful. Please complete payment within 3 days.",
                data = new
                {
                    auction.Id,
                    product.BuyoutPrice,
                    WinnerId = userId,
                    sale.Status,
                    sale.PaymentDeadline
                }
            });
        }
    }
}