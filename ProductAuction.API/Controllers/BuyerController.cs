
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Buyer")]
    public class BuyerController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BuyerController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Buyer/my-bids
        // Retrieves the logged-in buyer's bids and their auction status.
        [HttpGet("my-bids")]
        public async Task<IActionResult> GetMyBids()
        {
            // Get the logged-in buyer's ID from the JWT.
            var buyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (buyerId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // Join bids with their auctions and products.
            // Only return bids placed by the logged-in buyer.
            var bids = await _context.Bids
                .Where(b => b.BidderId == buyerId)
                .OrderByDescending(b => b.PlacedAt)
                .Select(b => new
                {
                    bidId = b.Id,
                    auctionId = b.AuctionId,
                    productId = b.Auction.Product.Id,
                    productTitle = b.Auction.Product.Title,
                    bidAmount = b.Amount,
                    placedAt = b.PlacedAt,
                    auctionStatus = b.Auction.Status,
                    auctionEndTime = b.Auction.EndTime
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your bids were retrieved successfully.",
                data = bids
            });
        }

        // GET: api/Buyer/bid-history
        // Retrieves the logged-in buyer's complete bid history.
        [HttpGet("bid-history")]
        public async Task<IActionResult> GetBidHistory()
        {
            var buyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (buyerId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var bids = await _context.Bids
                .Where(b => b.BidderId == buyerId)
                .OrderByDescending(b => b.PlacedAt)
                .Select(b => new
                {
                    bidId = b.Id,
                    auctionId = b.AuctionId,
                    productId = b.Auction.Product.Id,
                    productTitle = b.Auction.Product.Title,
                    bidAmount = b.Amount,
                    isBuyout = b.IsBuyout,
                    placedAt = b.PlacedAt,
                    auctionStatus = b.Auction.Status,
                    auctionEndTime = b.Auction.EndTime
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your bid history was retrieved successfully.",
                data = bids
            });
        }

        // GET: api/Buyer/won-auctions
        // Retrieves auctions won by the logged-in buyer.
        [HttpGet("won-auctions")]
        public async Task<IActionResult> GetWonAuctions()
        {
            var buyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (buyerId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var auctions = await _context.Auctions
                .Where(a => a.WinnerId == buyerId)
                .OrderByDescending(a => a.EndTime)
                .Select(a => new
                {
                    auctionId = a.Id,
                    productId = a.Product.Id,
                    productTitle = a.Product.Title,
                    winningAmount = a.CurrentHighestBid,
                    auctionStatus = a.Status,
                    result = a.Result,
                    startTime = a.StartTime,
                    endTime = a.EndTime,

                    saleId = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefault(),

                    paymentStatus = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.Status)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your won auctions were retrieved successfully.",
                data = auctions
            });
        }
    }
}
