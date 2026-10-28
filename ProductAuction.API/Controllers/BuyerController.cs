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


        // GET: api/Buyer/dashboard
        // Returns aggregated stats for the buyer dashboard:
        // bids, won auctions, total spent, and wallet balances.
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            //  bid stats
            // grab all of this user's bids along with their auctions
            var bids = await _context.Bids
                .Where(b => b.BidderId == userId)
                .Include(b => b.Auction)
                .ToListAsync();

            // count active ones, and how many the user is currently winning
            var activeBids = bids.Count(b => b.Auction.Status == "Active");

            var leadingBids = bids.Count(b =>
                b.Auction.Status == "Active" &&
                b.Auction.CurrentHighestBidderId == userId);

            //  sales & won auctions
            // sales where this user is the buyer
            var sales = await _context.Sales
                .Where(s => s.BuyerId == userId)
                .ToListAsync();

            var wonCount = sales.Count;

            var awaitingPayment = sales.Count(s =>
                s.Status == "AwaitingPayment" ||
                s.Status == "PaymentOverdue");

            var totalSpent = sales
                .Where(s => s.Status == "Completed" || s.Status == "Paid")
                .Sum(s => s.FinalAmount);

            //  wallet stats
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            var availableBalance = wallet != null ? (wallet.Balance - wallet.ReservedBalance) : 0;
            var reservedBalance = wallet?.ReservedBalance ?? 0;

            return Ok(new
            {
                status = 200,
                data = new
                {
                    activeBids,
                    leadingBids,
                    wonCount,
                    awaitingPayment,
                    totalSpent,
                    availableBalance,
                    reservedBalance
                }
            });
        }


        // GET: api/Buyer/my-bids
        // Returns the logged-in buyer's bids and the status of each auction.
        [HttpGet("my-bids")]
        public async Task<IActionResult> GetMyBids()
        {
            // grab the buyer id from the JWT
            var buyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (buyerId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // only return bids placed by this buyer
            var bids = await _context.Bids
                .Where(b => b.BidderId == buyerId)
                .OrderByDescending(b => b.PlacedAt)
                .Select(b => new
                {
                    bidId = b.Id,
                    auctionId = b.AuctionId,
                    productId = b.Auction.Product.Id,
                    productTitle = b.Auction.Product.Title,

                    // main product image (blank if none)
                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == b.Auction.Product.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",

                    bidAmount = b.Amount,
                    placedAt = b.PlacedAt,
                    auctionStatus = b.Auction.Status,
                    auctionEndTime = b.Auction.EndTime,

                    // is this bid still the leading one?
                    isHighest = b.Auction.CurrentHighestBidderId == buyerId
                        && b.Auction.CurrentHighestBid == b.Amount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your bids were retrieved successfully.",
                data = bids
            });
        }


        // GET: api/Buyer/won-auctions
        // Returns auctions this buyer won, plus sale / dispatch / dispute info for each.
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

                    // main product image (blank if none)
                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.Product.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",

                    winningAmount = a.CurrentHighestBid,
                    auctionStatus = a.Status,
                    result = a.Result,
                    startTime = a.StartTime,
                    endTime = a.EndTime,

                    // sale record for this auction + buyer (may not exist yet)
                    saleId = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefault(),

                    paymentStatus = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.Status)
                        .FirstOrDefault() ?? "AwaitingPayment",

                    // delivery info — only exists after the seller dispatches
                    dispatchInfo = _context.DeliveryRecords
                        .Where(d => d.SaleId == _context.Sales
                            .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                            .Select(s => s.Id)
                            .FirstOrDefault())
                        .Select(d => d.DispatchInfo)
                        .FirstOrDefault(),

                    trackingEvidence = _context.DeliveryRecords
                        .Where(d => d.SaleId == _context.Sales
                            .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                            .Select(s => s.Id)
                            .FirstOrDefault())
                        .Select(d => d.TrackingEvidence)
                        .FirstOrDefault(),

                    // dispute for the sale, if the buyer filed one
                    dispute = _context.Disputes
                        .Where(d => _context.Sales.Any(s =>
                            s.AuctionId == a.Id &&
                            s.BuyerId == buyerId &&
                            s.Id == d.SaleId))
                        .Select(d => new
                        {
                            id = d.Id,
                            complaint = d.Complaint,
                            evidenceUrl = d.EvidenceUrl,
                            status = d.Status,
                            adminDecision = d.AdminDecision,
                            createdAt = d.CreatedAt
                        })
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