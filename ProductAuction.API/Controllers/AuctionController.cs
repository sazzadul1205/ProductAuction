using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs;
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

        public AuctionController(AppDbContext context)
        {
            _context = context;
        }


        // GET: api/auction
        // Returns all auctions that are either already started or have a start time set.
        [HttpGet]
        public async Task<IActionResult> GetAuctions([FromQuery] PaginationQueryDto query)
        {
            // Normalize the page number — never below 1
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size — default 20, hard cap at 100
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Free-text search term (may be null or empty)
            var search = query.Search;

            // Optional auction-status filter: Active / Upcoming / Ended / All
            var filter = query.Filter;

            var auctionsQuery = _context.Auctions
                .Where(a => a.Status != "Upcoming" || a.StartTime.HasValue)
                .AsQueryable();

            // apply status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "Active" || filter == "Live")
                {
                    auctionsQuery = auctionsQuery.Where(a => a.Status == "Active");
                }
                else if (filter == "Upcoming")
                {
                    auctionsQuery = auctionsQuery.Where(a => a.Status == "Upcoming");
                }
                else if (filter == "Ended")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == "Completed" || a.Status == "Cancelled");
                }
                else
                {
                    auctionsQuery = auctionsQuery.Where(a => a.Status == filter);
                }
            }

            // apply search filter — matches product title
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                auctionsQuery = auctionsQuery.Where(a =>
                    a.Product.Title.Contains(term));
            }

            // apply category filter
            if (query.CategoryId.HasValue)
            {
                auctionsQuery = auctionsQuery.Where(a => a.Product.CategoryId == query.CategoryId.Value);
            }

            auctionsQuery = auctionsQuery.OrderByDescending(a => a.Id);

            var totalCount = await auctionsQuery.CountAsync();

            // Banner guarantee: if filters returned zero results, fall back to
            // all auctions so the home banner always has at least one product.
            var bannerFallback = totalCount == 0;
            if (bannerFallback)
            {
                auctionsQuery = _context.Auctions
                    .Where(a => a.Status != "Upcoming" || a.StartTime.HasValue)
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
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Upcoming" && a.StartTime.HasValue)
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
        // Returns full auction details (public view).
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
        // Returns auction details for the seller who owns it (extra fields like cancellation reason).
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
            var auction = await _context.Auctions.FindAsync(id);

            if (auction == null)
            {
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

            return Ok(new
            {
                status = 200,
                message = "Auction schedule configured successfully."
            });
        }

        // PUT: api/auction/{id}/cancel
        // Sellers can cancel upcoming auctions (deposit forfeited);
        // admins can cancel upcoming or active auctions (all reservations released).
        [HttpPut("{id}/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelAuction(int id, CancelAuctionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
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

            // all the wallet changes below need to happen together or not at all
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
                return Forbid();

            if (auction.Status != "Upcoming" && auction.Status != "Active")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming or active auctions can be cancelled."
                });
            }

            // sellers can only cancel before the auction starts
            if (isSeller && auction.Status != "Upcoming")
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
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved");

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
                var sellerWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

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
                    // seller cancelling = deposit is forfeited to the platform
                    if (sellerWallet.Balance < reservation.Amount)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Insufficient wallet balance to forfeit the deposit."
                        });
                    }

                    var platformUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.UserName == "platform-system-account");

                    if (platformUser == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The platform account was not found."
                        });
                    }

                    var platformWallet = await _context.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

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
                }
                else
                {
                    // admin cancelling = just release the deposit back to the seller
                    sellerWallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = "Released";
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
                    var buyerWallet = await _context.Wallets
                        .FirstOrDefaultAsync(w => w.Id == buyerReservation.WalletId);

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
                    buyerReservation.Status = "Released";
                }
            }

            auction.Status = "Cancelled";
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
                    auction.Status,
                    auction.CancellationReason
                }
            });
        }
    }
}