using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs;
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

        // Get: api/auction
        // View all auctions.
        [HttpGet]
        public async Task<IActionResult> GetAuctions()
        {
            var auctions = await _context.Auctions
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,
                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Auctions retrieved successfully.",
                data = auctions
            });
        }


        // Get: api/auction/my-auctions
        // View auctions created by the logged-in user.
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

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your auctions retrieved successfully.",
                data = auctions
            });
        }


        // Get: api/auction/active
        // View active auctions.
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Active")
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Active auctions retrieved successfully.",
                data = auctions
            });
        }

        // Get: api/auction/upcoming
        // View upcoming auctions.
        [HttpGet("upcoming")]
        public async Task<IActionResult> GetUpcomingAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Upcoming")
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Upcoming auctions retrieved successfully.",
                data = auctions
            });
        }

        // Get: api/auction/completed
        // View completed auctions.
        [HttpGet("completed")]
        public async Task<IActionResult> GetCompletedAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Completed")
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Completed auctions retrieved successfully.",
                data = auctions
            });
        }

        // Get: api/auction/cancelled
        // View cancelled auctions.
        [HttpGet("cancelled")]
        public async Task<IActionResult> GetCancelledAuctions()
        {
            var auctions = await _context.Auctions
                .Where(a => a.Status == "Cancelled")
                .Select(a => new AuctionListDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Cancelled auctions retrieved successfully.",
                data = auctions
            });
        }

        // GET: api/auction/{id}
        // View details of one auction.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAuctionById(int id)
        {
            var auction = await _context.Auctions
                .Where(a => a.Id == id)
                .Select(a => new AuctionDetailsDto
                {
                    Id = a.Id,
                    ProductId = a.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    ProductDescription = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Description)
                        .FirstOrDefault() ?? string.Empty,

                    StartingPrice = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.StartingPrice)
                        .FirstOrDefault(),

                    BuyoutPrice = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.BuyoutPrice)
                        .FirstOrDefault(),

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount,
                    Result = a.Result
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


        // Get: api/auction/product/{productId}
        // View auctions for a specific product.
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

                    ProductTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    Status = a.Status,
                    CurrentHighestBid = a.CurrentHighestBid,
                    BidCount = a.BidCount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Product auctions retrieved successfully.",
                data = auctions
            });
        }

        // Put: api/auction/{id}/configure
        // Admin configures the auction schedule.
        [HttpPut("{id}/configure")]
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

        // PUT: api/auction/1/schedule
        // Update an upcoming auction's schedule.
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

            // Only the user who created the auction can update it for now.
            if (auction.SellerId != userId)
            {
                return Forbid();
            }

            if (auction.Status != "Upcoming")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming auctions can be rescheduled."
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
        // Sellers can cancel upcoming auctions and forfeit their deposit.
        // Admins can cancel upcoming or active auctions and release reservations.
        [HttpPut("{id}/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelAuction(int id, CancelAuctionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A cancellation reason is required."
                });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

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

            if (auction.Status != "Upcoming" &&  auction.Status != "Active")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming or active auctions can be cancelled."
                });
            }

            // Sellers can cancel only upcoming auctions.
            if (isSeller && auction.Status != "Upcoming")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Sellers can only cancel upcoming auctions."
                });
            }

            // Find this seller's reserved deposit for this product.
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
                    // Forfeit the entire deposit to the platform.
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

                    reservation.Status = "Forfeited";

                    // Record the debit from the seller.
                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = sellerWallet.Id,
                        Type = "Penalty",
                        Amount = reservation.Amount,
                        BalanceAfter = sellerWallet.Balance,
                        Reference = $"Seller deposit forfeited for auction #{auction.Id}",
                        CreatedAt = now
                    });

                    // Record the amount received by the platform.
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
                    // Admin cancellation: release the seller's deposit.
                    sellerWallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = "Released";
                }
            }

            // Admin cancellation also releases buyer eligibility reservations.
            // Seller cancellation is allowed only for Upcoming auctions.
            if (isAdmin)
            {
                var buyerReservations = await _context.WalletReservations
                    .Where(r =>
                        r.AuctionId == auction.Id &&
                        r.Type == "BuyerFunds" &&
                        r.Status == "Reserved")
                    .ToListAsync();

                foreach (var buyerReservation in buyerReservations)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w =>w.Id == buyerReservation.WalletId);

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