using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auction;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Product;
using ProductAuction.API.Models;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuctionController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AuctionController> _logger;

        public AuctionController(AppDbContext context, ILogger<AuctionController> logger)
        {
            _context = context;
            _logger = logger;
        }


        // GET: api/auction
        // Returns all auctions with support for search, category filtering, and status tabs.
        [HttpGet]
        public async Task<IActionResult> GetAuctions([FromQuery] PaginationQueryDto query)
        {
            // Normalize pagination
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

            var search = query.Search;
            var filter = query.Filter;
            var now = DateTime.UtcNow;

            // Base query
            var auctionsQuery = _context.Auctions
                .Where(a => a.StartTime.HasValue)
                .AsQueryable();

            // Apply Status Filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "Active" || filter == "Live")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == "Active" &&
                        (!a.EndTime.HasValue || a.EndTime > now));
                }
                else if (filter == "Upcoming")
                {
                    // Must be marked Upcoming AND the end time hasn't passed yet
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == "Upcoming" &&
                        a.StartTime > now &&
                        (!a.EndTime.HasValue || a.EndTime > now));
                }
                else if (filter == "Ended")
                {
                    // Show anything marked Completed/Cancelled OR anything whose time has run out
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == "Completed" ||
                        a.Status == "Cancelled" ||
                        (a.EndTime.HasValue && a.EndTime <= now));
                }
                else
                {
                    auctionsQuery = auctionsQuery.Where(a => a.Status == filter);
                }
            }

            //  Apply Search Filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                auctionsQuery = auctionsQuery.Where(a => a.Product.Title.Contains(term));
            }

            //  Apply Category Filter
            if (query.CategoryId.HasValue)
            {
                auctionsQuery = auctionsQuery.Where(a => a.Product.CategoryId == query.CategoryId.Value);
            }

            // Sort
            auctionsQuery = auctionsQuery
                .OrderBy(a => a.Status == "Active" ? 1 :
                              a.Status == "Upcoming" ? 2 :
                              (a.Status == "Completed" || a.Status == "Cancelled") ? 3 : 4)
                .ThenByDescending(a => a.Id);

            var totalCount = await auctionsQuery.CountAsync();

            //  Home-Banner Fallback Logic
            var isDefaultHomeRequest =
                string.IsNullOrWhiteSpace(filter) &&
                string.IsNullOrWhiteSpace(search) &&
                !query.CategoryId.HasValue &&
                page == 1;

            var bannerFallback = isDefaultHomeRequest && totalCount == 0;

            if (bannerFallback)
            {
                auctionsQuery = _context.Auctions
                    .Where(a => a.Status != "Upcoming" || a.StartTime.HasValue)
                    .OrderByDescending(a => a.Id);
                totalCount = await auctionsQuery.CountAsync();
            }

            // Execute Query
            var auctions = await auctionsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",
                    CategoryId = a.Product.CategoryId,
                    CategoryName = a.Product.Category.Name
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Auctions retrieved successfully.",
                data = new
                {
                    items = auctions,
                    page,
                    pageSize,
                    totalCount
                }
            });
        }


        // GET: api/auction/active
        // Returns auctions that are currently running.
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveAuctions()
        {

            var auctions = await _context.Auctions
                .Where(a => a.Status == "Active")
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",
                    CategoryId = a.Product.CategoryId,
                    CategoryName = a.Product.Category.Name
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Active auctions retrieved successfully.",
                data = auctions
            });
        }


        // GET: api/auction/upcoming
        // Returns auctions that haven't started yet but have a start time.
        [HttpGet("upcoming")]
        public async Task<IActionResult> GetUpcomingAuctions()
        {
            var now = DateTime.UtcNow;

            var auctions = await _context.Auctions
                .Where(a =>
                    a.Status == "Upcoming" &&
                    a.StartTime.HasValue &&
                    a.EndTime > now)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",
                    CategoryId = a.Product.CategoryId,
                    CategoryName = a.Product.Category.Name
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Upcoming auctions retrieved successfully.",
                data = auctions
            });
        }

        // GET: api/auction/{id}
        // Returns full auction details
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAuctionById(int id)
        {

            var auction = await _context.Auctions
                .Where(a => a.Id == id)
                .Select(a => new AuctionDetailsDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    ProductDescription = a.Product.Description,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    Result = a.Result,
                    Brand = a.Product.Brand,
                    ModelNumber = a.Product.ModelNumber,
                    Condition = a.Product.Condition,
                    CategoryName = a.Product.Category.Name,
                    PurchaseDate = a.Product.PurchaseDate,
                    UsageHistory = a.Product.UsageHistory,
                    ReasonForSelling = a.Product.ReasonForSelling,
                    KnownIssues = a.Product.KnownIssues,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",
                    Images = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId)
                        .Select(i => new ProductImageResponseDto
                        {
                            Id = i.Id,
                            ImageUrl = i.ImageUrl,
                            IsMain = i.IsMain
                        }).ToList()
                })
                .FirstOrDefaultAsync();

            if (auction == null)
            {

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Auction details retrieved successfully.",
                data = auction
            });
        }


        // GET: api/auction/{id}/seller
        // Returns auction details for the seller who owns it
        [HttpGet("{id}/seller")]
        [Authorize]
        public async Task<IActionResult> GetAuctionByIdForSeller(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            var auction = await _context.Auctions
                .Where(a => a.Id == id && a.SellerId == userId)
                .Select(a => new AuctionDetailsDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    ProductDescription = a.Product.Description,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    Result = a.Result,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",
                    Images = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId)
                        .Select(i => new ProductImageResponseDto
                        {
                            Id = i.Id,
                            ImageUrl = i.ImageUrl,
                            IsMain = i.IsMain
                        }).ToList(),
                    CancellationReason = a.CancellationReason,
                    Brand = a.Product.Brand,
                    ModelNumber = a.Product.ModelNumber,
                    Condition = a.Product.Condition,
                    CategoryName = a.Product.Category.Name,
                    PurchaseDate = a.Product.PurchaseDate,
                    UsageHistory = a.Product.UsageHistory,
                    ReasonForSelling = a.Product.ReasonForSelling,
                    KnownIssues = a.Product.KnownIssues
                })
                .FirstOrDefaultAsync();

            if (auction == null)
            {

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Auction details retrieved successfully.",
                data = auction
            });
        }


        // PUT: api/auction/{id}/configure
        // Admin sets the start and end time for an upcoming auction.
        [HttpPut("{id}/configure")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ConfigureAuction(int id, UpdateAuctionScheduleDto dto)
        {
            // _logger.LogInformation("ConfigureAuction called for auction #{AuctionId} (start={Start}, end={End}).",
            //     id, dto.StartTime, dto.EndTime);

            var auction = await _context.Auctions.FindAsync(id);

            if (auction == null)
            {
                // _logger.LogWarning("ConfigureAuction: auction #{AuctionId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // only upcoming auctions can be configured
            if (auction.Status != "Upcoming")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming auctions can be configured."
                });
            }

            if (dto.StartTime <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Start time must be in the future."
                });
            }

            if (dto.EndTime <= dto.StartTime)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "End time must be after the start time."
                });
            }

            auction.StartTime = dto.StartTime;
            auction.EndTime = dto.EndTime;

            await _context.SaveChangesAsync();

            // _logger.LogInformation("ConfigureAuction: auction #{AuctionId} scheduled successfully.", id);

            return Ok(new
            {
                status = 200,
                message = "Auction schedule configured successfully."
            });
        }

        // PUT: api/auction/{id}/cancel
        // Sellers can cancel upcoming auctions 
        // admins can cancel upcoming or active auctions 
        [HttpPut("{id}/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelAuction(int id, CancelAuctionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");

            if (string.IsNullOrEmpty(userId))
            {
                // _logger.LogWarning("CancelAuction: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            if (string.IsNullOrEmpty(dto.Reason))
            {
                // _logger.LogWarning("CancelAuction: cancellation reason is required for auction #{AuctionId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "A cancellation reason is required."
                });
            }

            // _logger.LogInformation("CancelAuction called for auction #{AuctionId} by user {UserId} (isAdmin={IsAdmin}).", id, userId, isAdmin);

            // all the wallet changes below need to happen together or not at all
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == id);

            if (auction == null)
            {
                // _logger.LogWarning("CancelAuction: auction #{AuctionId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            var isSeller = auction.SellerId == userId;

            if (!isSeller && !isAdmin)
            {

                return Forbid();
            }

            if (auction.Status != "Upcoming" && auction.Status != "Active")
            {
                // _logger.LogWarning("CancelAuction: auction #{AuctionId} status {Status} cannot be cancelled.", id, auction.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming or active auctions can be cancelled."
                });
            }

            // sellers can only cancel before the auction starts
            if (isSeller && auction.Status != "Upcoming")
            {
                // _logger.LogWarning("CancelAuction: seller cannot cancel active auction #{AuctionId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Sellers can only cancel upcoming auctions."
                });
            }

            // look up the seller's deposit reservation for this product
            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == auction.ProductId &&
                    r.WalletId == _context.Wallets
                        .Where(w => w.UserId == auction.SellerId)
                        .Select(w => w.Id)
                        .FirstOrDefault() &&
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved");

            // a seller without a reserved deposit has nothing to forfeit 
            if (isSeller && reservation == null)
            {
                // _logger.LogWarning("CancelAuction: no reserved seller deposit found for auction #{AuctionId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "No reserved seller deposit was found."
                });
            }

            if (reservation != null)
            {
                var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

                if (sellerWallet == null)
                {
                    // _logger.LogError("CancelAuction: seller wallet #{WalletId} not found for auction #{AuctionId}.", reservation.WalletId, id);

                    return BadRequest(new
                    {
                        status = 400,
                        message = "Seller wallet not found."
                    });
                }

                if (sellerWallet.ReservedBalance < reservation.Amount)
                {
                    // _logger.LogWarning("CancelAuction: insufficient reserved balance on seller wallet #{WalletId}.", sellerWallet.Id);

                    return BadRequest(new
                    {
                        status = 400,
                        message = "Insufficient reserved balance to process the deposit."
                    });
                }

                if (isSeller)
                {
                    // seller cancelling 
                    if (sellerWallet.Balance < reservation.Amount)
                    {
                        // _logger.LogWarning("CancelAuction: seller wallet #{WalletId} balance insufficient to forfeit deposit.", sellerWallet.Id);

                        return BadRequest(new
                        {
                            status = 400,
                            message = "Insufficient wallet balance to forfeit the deposit."
                        });
                    }

                    var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");

                    var platformWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

                    if (platformWallet == null)
                    {
                        // _logger.LogError("CancelAuction: platform wallet for user {PlatformUserId} not found.", platformUser.Id);

                        return BadRequest(new
                        {
                            status = 400,
                            message = "The platform wallet was not found."
                        });
                    }

                    var now = DateTime.UtcNow;

                    sellerWallet.Balance -= reservation.Amount;
                    sellerWallet.ReservedBalance -= reservation.Amount;
                    platformWallet.Balance += reservation.Amount;
                    reservation.Status = "Forfeited";

                    // record both sides of the transfer
                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = sellerWallet.Id,
                        Type = "Penalty",
                        Amount = reservation.Amount,
                        BalanceAfter = sellerWallet.Balance,
                        Reference = $"Seller deposit forfeited for auction #{auction.Id}",
                        CreatedAt = now
                    });

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = platformWallet.Id,
                        Type = "Credit",
                        Amount = reservation.Amount,
                        BalanceAfter = platformWallet.Balance,
                        Reference = $"Seller deposit forfeiture for auction #{auction.Id}",
                        CreatedAt = now
                    });

                    // _logger.LogInformation("CancelAuction: seller deposit of {Amount} forfeited to platform for auction #{AuctionId}.",
                    //     reservation.Amount, auction.Id);
                }
                else
                {
                    // admin cancelling
                    sellerWallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = "Released";

                    // _logger.LogInformation("CancelAuction: seller deposit released by admin for auction #{AuctionId}.", auction.Id);
                }
            }

            if (isAdmin)
            {
                // release every buyer's reserved funds for this auction
                var buyerReservations = await _context.WalletReservations
                    .Where(r => r.AuctionId == auction.Id &&
                                r.Type == "BuyerFunds" &&
                                r.Status == "Reserved")
                    .ToListAsync();

                foreach (var buyerReservation in buyerReservations)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == buyerReservation.WalletId);

                    if (buyerWallet == null)
                    {
                        // _logger.LogError("CancelAuction: buyer wallet #{WalletId} not found for auction #{AuctionId}.",
                        //     buyerReservation.WalletId, auction.Id);

                        return BadRequest(new
                        {
                            status = 400,
                            message = "A buyer wallet associated with a reservation was not found."
                        });
                    }

                    if (buyerWallet.ReservedBalance < buyerReservation.Amount)
                    {
                        // _logger.LogWarning("CancelAuction: insufficient reserved balance on buyer wallet #{WalletId}.", buyerWallet.Id);

                        return BadRequest(new
                        {
                            status = 400,
                            message = "Insufficient reserved balance to release a buyer reservation."
                        });
                    }

                    buyerWallet.ReservedBalance -= buyerReservation.Amount;
                    buyerReservation.Status = "Released";
                }

                // _logger.LogInformation("CancelAuction: released {Count} buyer reservation(s) for auction #{AuctionId}.",
                //     buyerReservations.Count, auction.Id);
            }

            auction.Status = "Cancelled";
            auction.CancellationReason = dto.Reason.Trim();

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // _logger.LogInformation("CancelAuction: auction #{AuctionId} cancelled successfully (bySeller={IsSeller}).", auction.Id, isSeller);

            return Ok(new
            {
                status = 200,
                message = isSeller
                    ? "Auction cancelled. Your deposit has been forfeited to the platform."
                    : "Auction cancelled by admin. Seller and buyer reservations have been released.",
                data = new
                {
                    auction.Id,
                    auction.Status,
                    auction.CancellationReason
                }
            });
        }
    }
}