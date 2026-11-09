using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auction;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Product;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;
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
                        a.Status == AuctionStatus.Active &&
                        (!a.EndTime.HasValue || a.EndTime > now));
                }
                else if (filter == "Upcoming")
                {
                    // Must be marked Upcoming AND the end time hasn't passed yet
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == AuctionStatus.Upcoming &&
                        a.StartTime > now &&
                        (!a.EndTime.HasValue || a.EndTime > now));
                }
                else if (filter == "Ended")
                {
                    // Show anything marked Completed/Cancelled OR anything whose time has run out
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == AuctionStatus.Completed ||
                        a.Status == AuctionStatus.Cancelled ||
                        (a.EndTime.HasValue && a.EndTime <= now));
                }
                else if (Enum.TryParse<AuctionStatus>(filter, ignoreCase: true, out var parsedStatus))
                {
                    auctionsQuery = auctionsQuery.Where(a => a.Status == parsedStatus);
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
                .OrderBy(a => a.Status == AuctionStatus.Active ? 1 :
                              a.Status == AuctionStatus.Upcoming ? 2 :
                              (a.Status == AuctionStatus.Completed || a.Status == AuctionStatus.Cancelled) ? 3 : 4)
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
                    .Where(a => a.Status != AuctionStatus.Upcoming || a.StartTime.HasValue)
                    .OrderByDescending(a => a.Id);
                totalCount = await auctionsQuery.CountAsync();
            }

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
                    Status = a.Status.ToString(),
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
                .Where(a => a.Status == AuctionStatus.Active)
                .OrderByDescending(a => a.EndTime)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status.ToString(),
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


        // GET: api/auction/home
        // Returns banner, up to 8 active, and up to 8 upcoming auctions for the home page.
        [HttpGet("home")]
        public async Task<IActionResult> GetHomeAuctions([FromQuery] string? search, [FromQuery] int? categoryId)
        {
            var now = DateTime.UtcNow;

            var allQuery = _context.Auctions.Where(a => a.StartTime.HasValue).AsQueryable();

            // Banner is fixed (not affected by search/category filters)
            var banner = await allQuery
                .Where(a => (a.Status == AuctionStatus.Active && (!a.EndTime.HasValue || a.EndTime > now)) ||
                            (a.Status == AuctionStatus.Upcoming && a.StartTime > now && (!a.EndTime.HasValue || a.EndTime > now)))
                .OrderBy(a => a.Status == AuctionStatus.Active ? 0 : 1)
                .ThenBy(a => a.Status == AuctionStatus.Active ? a.EndTime : a.StartTime)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status.ToString(),
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",
                    CategoryId = a.Product.CategoryId,
                    CategoryName = a.Product.Category.Name
                })
                .FirstOrDefaultAsync();

            var bannerId = banner?.Id;

            var baseQuery = allQuery;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                baseQuery = baseQuery.Where(a => a.Product.Title.Contains(term));
            }
            if (categoryId.HasValue)
                baseQuery = baseQuery.Where(a => a.Product.CategoryId == categoryId.Value);

            var active = await baseQuery
                .Where(a => a.Status == AuctionStatus.Active && (!a.EndTime.HasValue || a.EndTime > now) && a.Id != bannerId)
                .OrderBy(a => a.EndTime)
                .Take(8)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status.ToString(),
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

            var upcoming = await baseQuery
                .Where(a => a.Status == AuctionStatus.Upcoming && a.StartTime > now && (!a.EndTime.HasValue || a.EndTime > now) && a.Id != bannerId)
                .OrderBy(a => a.StartTime)
                .Take(8)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status.ToString(),
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
                message = "Home auctions retrieved successfully.",
                data = new { banner, active, upcoming }
            });
        }
        
        public async Task<IActionResult> GetUpcomingAuctions()
        {
            var now = DateTime.UtcNow;

            var auctions = await _context.Auctions
                .Where(a =>
                    a.Status == AuctionStatus.Upcoming &&
                    a.StartTime.HasValue &&
                    a.EndTime > now)
                .OrderBy(a => a.StartTime)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status.ToString(),
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
                    Status = a.Status.ToString(),
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    Result = a.Result != null ? a.Result.ToString() : null,
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
                    message = "User identity was not found."
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
                    Status = a.Status.ToString(),
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    Result = a.Result != null ? a.Result.ToString() : null,
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
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> ConfigureAuction(int id, UpdateAuctionScheduleDto dto)
        {
            var auction = await _context.Auctions.FindAsync(id);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            if (auction.Status != AuctionStatus.Upcoming)
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
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Super Admin");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found."
                });
            }

            if (string.IsNullOrEmpty(dto.Reason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A cancellation reason is required."
                });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == id);

            if (auction == null)
            {
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

            if (auction.Status != AuctionStatus.Upcoming && auction.Status != AuctionStatus.Active)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming or active auctions can be cancelled."
                });
            }

            // sellers can only cancel before the auction starts
            if (isSeller && auction.Status != AuctionStatus.Upcoming)
            {
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
                    r.Type == ReservationType.SellerDeposit &&
                    r.Status == ReservationStatus.Reserved);


            if (isSeller && reservation == null)
            {
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
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Seller wallet not found."
                    });
                }

                if (sellerWallet.ReservedBalance < reservation.Amount)
                {
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
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Insufficient wallet balance to forfeit the deposit."
                        });
                    }

                    var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");
                    if (platformUser == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The platform account was not found."
                        });
                    }

                    var platformWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

                    if (platformWallet == null)
                    {
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
                    reservation.Status = ReservationStatus.Forfeited;

                    // record both sides of the transfer
                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = sellerWallet.Id,
                        Type = WalletTransactionType.Penalty,
                        Amount = reservation.Amount,
                        BalanceAfter = sellerWallet.Balance,
                        Reference = $"Seller deposit forfeited for auction #{auction.Id}",
                        CreatedAt = now
                    });

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = platformWallet.Id,
                        Type = WalletTransactionType.Credit,
                        Amount = reservation.Amount,
                        BalanceAfter = platformWallet.Balance,
                        Reference = $"Seller deposit forfeiture for auction #{auction.Id}",
                        CreatedAt = now
                    });
                }
                else
                {
                    // admin cancelling
                    sellerWallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = ReservationStatus.Released;
                }
            }

            if (isAdmin)
            {
                // release every buyer's reserved funds for this auction
                var buyerReservations = await _context.WalletReservations
                    .Where(r => r.AuctionId == auction.Id &&
                                r.Type == ReservationType.BuyerFunds &&
                                r.Status == ReservationStatus.Reserved)
                    .ToListAsync();

                foreach (var buyerReservation in buyerReservations)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == buyerReservation.WalletId);

                    if (buyerWallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "A buyer wallet associated with a reservation was not found."
                        });
                    }

                    if (buyerWallet.ReservedBalance < buyerReservation.Amount)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Insufficient reserved balance to release a buyer reservation."
                        });
                    }

                    buyerWallet.ReservedBalance -= buyerReservation.Amount;
                    buyerReservation.Status = ReservationStatus.Released;
                }
            }

            auction.Status = AuctionStatus.Cancelled;
            auction.CancellationReason = dto.Reason.Trim();

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = isSeller
                    ? "Auction cancelled. Your deposit has been forfeited to the platform."
                    : "Auction cancelled by admin. Seller and buyer reservations have been released.",
                data = new
                {
                    auction.Id,
                    Status = auction.Status.ToString(),
                    auction.CancellationReason
                }
            });
        }

        // POST: api/auction/product/{productId}/reset-for-reauction
        [HttpPost("product/{productId}/reset-for-reauction")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> ResetForReAuction(int productId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null || product.SellerId != userId)
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });

            if (product.ApprovalStatus != ProductApprovalStatus.Approved || product.Status != ProductStatus.Available)
            {
                // allow reset if previous auction settled; normalize status
                if (product.ApprovalStatus == ProductApprovalStatus.Approved && product.Status == ProductStatus.Unavailable)
                {
                    product.Status = ProductStatus.Available;
                }
                else
                {
                    return BadRequest(new { status = 400, message = $"Product not eligible. ApprovalStatus={product.ApprovalStatus}, Status={product.Status}." });
                }
            }

            var hasLiveAuction = await _context.Auctions.AnyAsync(a =>
                a.ProductId == productId &&
                (a.Status == AuctionStatus.Active || a.Status == AuctionStatus.Upcoming));
            if (hasLiveAuction)
                return BadRequest(new
                {
                    status = 400,
                    message = "Product already has an active or upcoming auction."
                });

            var previous = await _context.Auctions
                .Where(a => a.ProductId == productId)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();
            if (previous == null)
                return BadRequest(new
                {
                    status = 400,
                    message = "This product has no previous auction."
                });

            var previousSale = await _context.Sales
                .FirstOrDefaultAsync(s => s.AuctionId == previous.Id);

            var eligible =
                previous.Result == AuctionResult.NoWinner ||
                (previous.Result == AuctionResult.Sold &&
                 previousSale != null &&
                 previousSale.Status == SaleStatus.PaymentOverdue);

            if (!eligible)
                return BadRequest(new
                {
                    status = 400,
                    message = "Previous auction has not settled into a re-actionable state."
                });

            product.ApprovalStatus = ProductApprovalStatus.Draft;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Product is back in Draft. Edit it, then submit for approval when ready."
            });
        }

        // GET: api/auction/product/{productId}
        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetAuctionsByProduct(int productId)
        {
            var auctions = await _context.Auctions
                .Where(a => a.ProductId == productId)
                .OrderByDescending(a => a.Id)
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = a.Product.Title,
                    StartingPrice = a.Product.StartingPrice,
                    BuyoutPrice = a.Product.BuyoutPrice,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status.ToString(),
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
                data = auctions
            });
        }

    }
}