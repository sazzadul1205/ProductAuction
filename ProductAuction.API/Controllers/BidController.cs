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
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);

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

            // The product must be approved before anyone can bid.
            if (product.ApprovalStatus != "Approved")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved for bidding."
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

            // A bid at or above the buyout price must use the buyout flow.
            //if (product.BuyoutPrice > 0 && dto.Amount >= product.BuyoutPrice)
            //{
            //    return BadRequest(new
            //    {
            //        status = 400,
            //        message = "Use the buyout option to purchase at the buyout price."
            //    });
            //}

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
                var availableBalance =
                    wallet.Balance - wallet.ReservedBalance;

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

        // GET: api/bid/auction/5
        // Gets the bid history for a specific auction.
        [HttpGet("auction/{auctionId}")]
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

            return Ok(new {
                status = 200,
                message = "Bids for this Auction Returned Successfully",
                data = bids
            });
        }


        // POST: api/bid/5/buyout
        // Allows a buyer to purchase a product immediately at its buyout price.
        [HttpPost("{auctionId}/buyout")]
        [Authorize]
        public async Task<IActionResult> Buyout(int auctionId)
        {
            // Get the logged-in user's ID.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized();
            }

            // Find the auction.
            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                return NotFound(new { message = "Auction not found." });
            }

            // The auction must currently be active.
            if (auction.Status != "Active")
            {
                return BadRequest(new { message = "This auction is not active." });
            }

            // The auction must not have passed its end time.
            if (!auction.EndTime.HasValue || auction.EndTime.Value <= DateTime.UtcNow)
            {
                return BadRequest(new { message = "This auction has ended." });
            }

            // Find the product being auctioned.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);

            if (product == null)
            {
                return NotFound(new { message = "Product not found." });
            }

            // Only approved products can be purchased.
            if (product.ApprovalStatus != "Approved")
            {
                return BadRequest(new { message = "This product is not approved." });
            }

            // The seller cannot buy their own product.
            if (product.SellerId == userId)
            {
                return BadRequest(new { message = "You cannot buy your own product." });
            }

            // Check whether this buyer already has a reserved eligibility amount.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return BadRequest(new { message = "Wallet not found." });
            }

            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.WalletId == wallet.Id &&
                    r.AuctionId == auctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved");

            // If the buyer has not participated before, check their available balance
            // and reserve 25% of the product's starting price.
            if (reservation == null)
            {
                decimal requiredAmount = product.StartingPrice * 0.25m;
                decimal availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    return BadRequest(new
                    {
                        message = "You do not have enough available balance to participate."
                    });
                }

                wallet.ReservedBalance += requiredAmount;

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

            // Record the buyout as a bid at the configured buyout price.
            var buyoutBid = new Bid
            {
                AuctionId = auction.Id,
                BidderId = userId,
                Amount = product.BuyoutPrice,
                IsBuyout = true,
                PlacedAt = DateTime.UtcNow
            };

            _context.Bids.Add(buyoutBid);

            // Complete the auction immediately and assign the buyer as winner.
            auction.Status = "Completed";
            auction.Result = "Sold";
            auction.WinnerId = userId;
            auction.CurrentHighestBid = product.BuyoutPrice;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            // Calculate the platform's 10% commission.
            decimal commissionAmount = Math.Round(product.BuyoutPrice * 0.10m, 2);

            // Calculate the seller's proceeds after commission.
            decimal sellerProceeds = product.BuyoutPrice - commissionAmount;

            // Create the sale record for the buyer.
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
                PaymentDeadline = DateTime.UtcNow.AddDays(3),
                CreatedAt = DateTime.UtcNow
            };

            _context.Sales.Add(sale);

            // Save the buyout bid, auction result, reservation, and sale.
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Buyout successful. Please complete payment within 3 days.",
                auctionId = auction.Id,
                buyoutPrice = product.BuyoutPrice,
                winnerId = userId,
                saleStatus = sale.Status,
                paymentDeadline = sale.PaymentDeadline
            });
        }

    }
}