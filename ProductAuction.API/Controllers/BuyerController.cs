using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.Models.Enums;
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
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            // grab all of this user's bids along with their auctions
            var bids = await _context.Bids
                .Where(b => b.BidderId == userId)
                .Include(b => b.Auction)
                .AsNoTracking()
                .ToListAsync();

            // count active ones, and how many the user is currently winning
            var activeBids = bids.Count(b => b.Auction.Status == AuctionStatus.Active);

            var leadingBids = bids.Count(b =>
                b.Auction.Status == AuctionStatus.Active &&
                b.Auction.CurrentHighestBidderId == userId);

            //  sales & won auctions
            var sales = await _context.Sales
                .Where(s => s.BuyerId == userId)
                .AsNoTracking()
                .ToListAsync();

            var wonCount = sales.Count;

            var awaitingPayment = sales.Count(s =>
                s.Status == SaleStatus.AwaitingPayment ||
                s.Status == SaleStatus.PaymentOverdue);

            var totalSpent = sales
                .Where(s => s.Status == SaleStatus.Completed || s.Status == SaleStatus.Paid)
                .Sum(s => s.FinalAmount);

            //  wallet stats
            var wallet = await _context.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId);

            var availableBalance = wallet != null ? (wallet.Balance - wallet.ReservedBalance) : 0;
            var reservedBalance = wallet?.ReservedBalance ?? 0;

            return Ok(new
            {
                status = 200,
                message = "Dashboard retrieved successfully.",
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
            var buyerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (buyerId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var page = query.Page;
            if (page < 1) page = 1;

            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var search = query.Search;
            var filter = query.Filter;

            var bidsQuery = _context.Bids
                .Where(b => b.BidderId == buyerId)
                .AsQueryable();

            // apply auction-status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "Active")
                {
                    bidsQuery = bidsQuery.Where(b => b.Auction.Status == AuctionStatus.Active);
                }
                else if (filter == "Ended")
                {
                    bidsQuery = bidsQuery.Where(b =>
                        b.Auction.Status == AuctionStatus.Completed ||
                        b.Auction.Status == AuctionStatus.Cancelled);
                }
                else if (Enum.TryParse<AuctionStatus>(filter, ignoreCase: true, out var parsedStatus))
                {
                    bidsQuery = bidsQuery.Where(b => b.Auction.Status == parsedStatus);
                }
            }

            // apply search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .AsNoTracking()
                    .Select(p => p.Id)
                    .ToListAsync();

                bidsQuery = bidsQuery.Where(b => matchingProductIds.Contains(b.Auction.ProductId));
            }

            bidsQuery = bidsQuery.OrderByDescending(b => b.PlacedAt);

            var totalCount = await bidsQuery.AsNoTracking().CountAsync();

            var totalBidsCount = await _context.Bids
                .AsNoTracking()
                .CountAsync(b => b.BidderId == buyerId);
            var activeBidsCount = await _context.Bids
                .AsNoTracking()
                .CountAsync(b =>
                b.BidderId == buyerId && b.Auction.Status == AuctionStatus.Active);
            var highestBidsCount = await _context.Bids
                .AsNoTracking()
                .CountAsync(b =>
                b.BidderId == buyerId
                && b.Auction.CurrentHighestBidderId == buyerId
                && b.Auction.CurrentHighestBid == b.Amount);

            var bids = await bidsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
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
                    auctionStatus = b.Auction.Status.ToString(),
                    auctionEndTime = b.Auction.EndTime,

                    isHighest = b.Auction.CurrentHighestBidderId == buyerId
                        && b.Auction.CurrentHighestBid == b.Amount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your bids were retrieved successfully.",
                data = bids,
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount
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
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

            var search = query.Search;
            var filter = query.Filter;

            var allWonAuctionIds = await _context.Auctions
                .Where(a => a.WinnerId == buyerId)
                .AsNoTracking()
                .Select(a => a.Id)
                .ToListAsync();

            var salesForBuyer = await _context.Sales
                .Where(s => s.BuyerId == buyerId && allWonAuctionIds.Contains(s.AuctionId))
                .AsNoTracking()
                .ToListAsync();

            var totalWon = allWonAuctionIds.Count;

            var pendingPaymentCount = salesForBuyer.Count(s =>
                s.Status == SaleStatus.AwaitingPayment || s.Status == SaleStatus.PaymentOverdue);

            var totalSpent = salesForBuyer
                .Where(s => s.Status == SaleStatus.Paid || s.Status == SaleStatus.Completed)
                .Sum(s => s.FinalAmount);

            var auctionsQuery = _context.Auctions
                .Where(a => a.WinnerId == buyerId)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();

                auctionsQuery = auctionsQuery.Where(a =>
                    a.Product.Title.ToLower().Contains(term));
            }

            var saleIds = salesForBuyer.Select(s => s.Id).ToList();

            var deliverySaleIds = await _context.DeliveryRecords
                .Where(d => saleIds.Contains(d.SaleId))
                .AsNoTracking()
                .Select(d => d.SaleId)
                .ToListAsync();

            var disputedSaleIds = await _context.Disputes
                .Where(d => saleIds.Contains(d.SaleId))
                .AsNoTracking()
                .Select(d => d.SaleId)
                .Distinct()
                .ToListAsync();

            // Apply status filters.
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "AwaitingPayment" || filter == "Action Required")
                {
                    var ids = salesForBuyer
                        .Where(s =>
                            s.Status == SaleStatus.AwaitingPayment ||
                            s.Status == SaleStatus.PaymentOverdue ||
                            (s.Status == SaleStatus.Paid && !deliverySaleIds.Contains(s.Id)))
                        .Select(s => s.AuctionId)
                        .ToList();

                    auctionsQuery = auctionsQuery.Where(a => ids.Contains(a.Id));
                }
                else if (filter == "Completed")
                {
                    var ids = salesForBuyer
                        .Where(s => s.Status == SaleStatus.Completed)
                        .Select(s => s.AuctionId)
                        .ToList();

                    auctionsQuery = auctionsQuery.Where(a => ids.Contains(a.Id));
                }
                else if (filter == "Disputed")
                {
                    var ids = salesForBuyer
                        .Where(s => disputedSaleIds.Contains(s.Id))
                        .Select(s => s.AuctionId)
                        .ToList();

                    auctionsQuery = auctionsQuery.Where(a => ids.Contains(a.Id));
                }
                else if (filter == "In Transit")
                {
                    var ids = salesForBuyer
                        .Where(s =>
                            deliverySaleIds.Contains(s.Id) &&
                            s.Status != SaleStatus.Completed)
                        .Select(s => s.AuctionId)
                        .ToList();

                    auctionsQuery = auctionsQuery.Where(a => ids.Contains(a.Id));
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

                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.Product.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),

                    winningAmount = a.CurrentHighestBid,
                    auctionStatus = a.Status.ToString(),
                    result = a.Result != null ? a.Result.ToString() : null,
                    startTime = a.StartTime,
                    endTime = a.EndTime,

                    saleId = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefault(),

                    paymentStatus = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.Status.ToString())
                        .FirstOrDefault() ?? "AwaitingPayment",

                    paymentDeadline = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.PaymentDeadline)
                        .FirstOrDefault(),

                    finalAmount = _context.Sales
                        .Where(s => s.AuctionId == a.Id && s.BuyerId == buyerId)
                        .Select(s => s.FinalAmount)
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
                            status = d.Status.ToString(),
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
                data = auctions,
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount
                }
            });
        }
    }
}