using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Buyer")]
    public class BuyerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BuyerController> _logger;

        public BuyerController(AppDbContext context, ILogger<BuyerController> logger)
        {
            _context = context;
            _logger = logger;
        }


        // GET: api/Buyer/dashboard
        // Returns aggregated stats for the buyer dashboard:
        // bids, won auctions, total spent, and wallet balances.
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                // _logger.LogWarning("GetDashboard: user identity missing from token.");

                return Unauthorized();
            }

            // _logger.LogInformation("GetDashboard called by user {UserId}.", userId);

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

            // _logger.LogInformation("GetDashboard for {UserId}: activeBids={ActiveBids}, leadingBids={LeadingBids}, wonCount={WonCount}, totalSpent={TotalSpent}.",
            //     userId, activeBids, leadingBids, wonCount, totalSpent);

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
        public async Task<IActionResult> GetMyBids([FromQuery] PaginationQueryDto query)
        {
            // grab the buyer id from the JWT
            var buyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (buyerId == null)
            {
                // _logger.LogWarning("GetMyBids: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional auction-status filter: Active / Ended
            var filter = query.Filter;

            // _logger.LogInformation("GetMyBids called by {BuyerId}: page={Page}, pageSize={PageSize}, filter={Filter}, search={Search}.",
            //     buyerId, page, pageSize, filter, search);

            // only return bids placed by this buyer
            var bidsQuery = _context.Bids
                .Where(b => b.BidderId == buyerId)
                .AsQueryable();

            // apply auction-status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "Active")
                {
                    bidsQuery = bidsQuery.Where(b => b.Auction.Status == "Active");
                }
                else if (filter == "Ended")
                {
                    bidsQuery = bidsQuery.Where(b => b.Auction.Status == "Completed" || b.Auction.Status == "Cancelled");
                }
                else
                {
                    bidsQuery = bidsQuery.Where(b => b.Auction.Status == filter);
                }
            }

            // apply search filter 
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                bidsQuery = bidsQuery.Where(b => matchingProductIds.Contains(b.Auction.ProductId));

                // _logger.LogDebug("GetMyBids: search '{Term}' matched {Count} product(s).", term, matchingProductIds.Count);
            }

            bidsQuery = bidsQuery.OrderByDescending(b => b.PlacedAt);

            var totalCount = await bidsQuery.CountAsync();

            // Global counts across ALL of this buyer's bids 
            var totalBidsCount = await _context.Bids.CountAsync(b =>
                b.BidderId == buyerId);
            var activeBidsCount = await _context.Bids.CountAsync(b =>
                b.BidderId == buyerId && b.Auction.Status == "Active");
            var highestBidsCount = await _context.Bids.CountAsync(b =>
                b.BidderId == buyerId
                && b.Auction.CurrentHighestBidderId == buyerId
                && b.Auction.CurrentHighestBid == b.Amount);

            var bids = await bidsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new
                {
                    bidId = b.Id,
                    auctionId = b.AuctionId,
                    productId = b.Auction.Product.Id,
                    productTitle = b.Auction.Product.Title,

                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == b.Auction.Product.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",

                    bidAmount = b.Amount,
                    placedAt = b.PlacedAt,
                    auctionStatus = b.Auction.Status,
                    auctionEndTime = b.Auction.EndTime,

                    isHighest = b.Auction.CurrentHighestBidderId == buyerId
                        && b.Auction.CurrentHighestBid == b.Amount
                })
                .ToListAsync();

            // _logger.LogInformation("GetMyBids returned {Count} of {Total} bid(s) for {BuyerId} (totalBids={TotalBids}, active={Active}, highest={Highest}).",
            //     bids.Count, totalCount, buyerId, totalBidsCount, activeBidsCount, highestBidsCount);

            return Ok(new
            {
                status = 200,
                message = "Your bids were retrieved successfully.",
                data = new
                {
                    items = bids,
                    page,
                    pageSize,
                    totalCount,
                    totalBidsCount,
                    activeBidsCount,
                    highestBidsCount
                }
            });
        }


        // GET: api/Buyer/won-auctions
        // Returns auctions this buyer won, plus sale / dispatch / dispute info for each.
        [HttpGet("won-auctions")]
        public async Task<IActionResult> GetWonAuctions([FromQuery] PaginationQueryDto query)
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

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);
        var search = query.Search;
        var filter = query.Filter;

        var auctionsQuery = _context.Auctions
            .Where(a => a.WinnerId == buyerId)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            auctionsQuery = auctionsQuery.Where(a => a.Product.Title.ToLower().Contains(term));
        }

        var allWonAuctionIds = await _context.Auctions
            .Where(a => a.WinnerId == buyerId)
            .Select(a => a.Id)
            .ToListAsync();

        var salesForBuyer = await _context.Sales
            .Where(s => s.BuyerId == buyerId && allWonAuctionIds.Contains(s.AuctionId))
            .ToListAsync();

        var totalWon = allWonAuctionIds.Count;
        var pendingPaymentCount = salesForBuyer.Count(s => s.Status == "AwaitingPayment" || s.Status == "PaymentOverdue");
        var totalSpent = salesForBuyer
            .Where(s => s.Status == "Paid" || s.Status == "Completed")
            .Sum(s => s.FinalAmount);

        // Apply filter based on derived status
        if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
        {
            var auctionIdsWithDisputes = await _context.Disputes
                .Where(d => salesForBuyer.Select(s => s.Id).Contains(d.SaleId))
                .Select(d => _context.Sales.Where(s => s.Id == d.SaleId).Select(s => s.AuctionId).FirstOrDefault())
                .Distinct()
                .ToListAsync();

            var deliveryBySaleId = await _context.DeliveryRecords
                .Where(dr => salesForBuyer.Select(s => s.Id).Contains(dr.SaleId))
                .ToDictionaryAsync(dr => dr.SaleId);

            if (filter == "AwaitingPayment")
            {
                var awaitingIds = salesForBuyer.Where(s => s.Status == "AwaitingPayment" || s.Status == "PaymentOverdue").Select(s => s.AuctionId).ToList();
                var paidNoDelivery = salesForBuyer.Where(s => (s.Status == "Paid") && !deliveryBySaleId.ContainsKey(s.Id)).Select(s => s.AuctionId).ToList();
                var allAction = awaitingIds.Union(paidNoDelivery).Distinct().ToList();
                auctionsQuery = auctionsQuery.Where(a => allAction.Contains(a.Id));
            }
            else if (filter == "Completed")
            {
                var ids = salesForBuyer.Where(s => s.Status == "Completed").Select(s => s.AuctionId).ToList();
                auctionsQuery = auctionsQuery.Where(a => ids.Contains(a.Id));
            }
            else if (filter == "Disputed")
            {
                auctionsQuery = auctionsQuery.Where(a => auctionIdsWithDisputes.Contains(a.Id));
            }
            else if (filter == "In Transit")
            {
                var inTransitSaleIds = deliveryBySaleId.Keys;
                var inTransitAuctionIds = salesForBuyer.Where(s => inTransitSaleIds.Contains(s.Id)).Select(s => s.AuctionId).ToList();
                auctionsQuery = auctionsQuery.Where(a => inTransitAuctionIds.Contains(a.Id) && !salesForBuyer.Any(s => s.AuctionId == a.Id && (s.Status == "Completed")));
            }
            else if (filter == "Action Required")
            {
                var awaitingIds = salesForBuyer.Where(s => s.Status == "AwaitingPayment" || s.Status == "PaymentOverdue").Select(s => s.AuctionId).ToList();
                var paidNoDelivery = salesForBuyer.Where(s => (s.Status == "Paid") && !deliveryBySaleId.ContainsKey(s.Id)).Select(s => s.AuctionId).ToList();
                var allAction = awaitingIds.Union(paidNoDelivery).Distinct().ToList();
                auctionsQuery = auctionsQuery.Where(a => allAction.Contains(a.Id));
            }
        }

        var totalCount = await auctionsQuery.CountAsync();

            var auctions = await auctionsQuery
                .OrderByDescending(a => a.EndTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    auctionId = a.Id,
                    productId = a.Product.Id,
                    productTitle = a.Product.Title,

                    // main product image (blank if none)
                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.Product.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),

                    winningAmount = a.CurrentHighestBid,
                    auctionStatus = a.Status,
                    result = a.Result,
                    startTime = a.StartTime,
                    endTime = a.EndTime,

                    // sale record for this auction + buyer
                    saleId = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefault(),

                    paymentStatus = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.Status)
                        .FirstOrDefault() ?? "AwaitingPayment",

                    paymentDeadline = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.PaymentDeadline)
                        .FirstOrDefault(),

                    finalAmount = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s =>s.FinalAmount)
                        .FirstOrDefault(),

                    deliveryRecord = _context.DeliveryRecords
                        .Where(d => _context.Sales
                            .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                            .Select(s => s.Id)
                            .Contains(d.SaleId))
                        .Select(d => new
                        {
                            d.Id,
                            dispatchedAt = d.DispatchedAt,
                            dispatchInfo = d.DispatchInfo,
                            trackingEvidence = d.TrackingEvidence
                        })
                        .FirstOrDefault(),

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
                            createdAt = d.CreatedAt,
                            raisedBy = d.RaisedBy
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Won auctions retrieved successfully.",
                data = new
                {
                    items = auctions,
                    page,
                    pageSize,
                    totalCount,
                    totalWon,
                    pendingPaymentCount,
                    totalSpent
                }
            });
        }
    }
}