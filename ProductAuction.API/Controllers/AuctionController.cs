using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs;
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
        public async Task<IActionResult> GetAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status != "Upcoming" || a.StartTime.HasValue)
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
                data = auctions
            });
        }


        // GET: api/auction/my-auctions
        // Returns only the auctions that belong to the logged-in user.
        [HttpGet("my-auctions")]
        public async Task<IActionResult> GetMyAuctions()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var auctions = await _context.Auctions
                .Where(a => a.SellerId == userId)
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
                message = "Your auctions retrieved successfully.",
                data = auctions
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


        // GET: api/auction/completed
        // Returns auctions that have finished.
        [HttpGet("completed")]
        public async Task<IActionResult> GetCompletedAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Completed")
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
                message = "Completed auctions retrieved successfully.",
                data = auctions
            });
        }


        // GET: api/auction/cancelled
        // Returns auctions that were cancelled.
        [HttpGet("cancelled")]
        public async Task<IActionResult> GetCancelledAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Cancelled")
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
                message = "Cancelled auctions retrieved successfully.",
                data = auctions
            });
        }


        // GET: api/auction/product/{productId}
        // Returns all auctions for a specific product.
        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetAuctionsByProduct(int productId)
        {
            var productExists = await _context.Products.AnyAsync(p => p.Id == productId);

            if (!productExists)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            var auctions = await _context.Auctions
                .Where(a => a.ProductId == productId)
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
                message = "Product auctions retrieved successfully.",
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


        // PUT: api/auction/{id}/schedule
        // Seller reschedules their own upcoming auction.
        [HttpPut("{id}/schedule")]
        public async Task<IActionResult> UpdateAuctionSchedule(int id, UpdateAuctionScheduleDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == id);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // only the seller who owns it can reschedule
            if (auction.SellerId != userId)
                return Forbid();

            if (auction.Status != "Upcoming")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming auctions can be rescheduled."
                });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction product not found."
                });
            }

            if (product.Status != "Available" || product.ApprovalStatus != "Approved")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The product must be approved and available to configure its auction."
                });
            }

            if (dto.StartTime >= dto.EndTime)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Start time must be earlier than end time."
                });
            }

            if (dto.StartTime <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Auction start time must be in the future."
                });
            }

            auction.StartTime = dto.StartTime;
            auction.EndTime = dto.EndTime;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Auction schedule updated successfully.",
                data = new
                {
                    auction.Id,
                    auction.StartTime,
                    auction.EndTime,
                    auction.Status
                }
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