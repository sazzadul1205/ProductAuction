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


        // PUT: api/auction/1/cancel
        // Cancel an upcoming auction.
        [HttpPut("{id}/cancel")]
        public async Task<IActionResult> CancelAuction(int id)
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

            // Only the user who created the auction can cancel it for now.
            if (auction.SellerId != userId)
            {
                return Forbid();
            }

            if (auction.Status != "Upcoming")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only upcoming auctions can be cancelled."
                });
            }

            auction.Status = "Cancelled";
            //auction.CancellationReason = dto.Reason;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Auction cancelled successfully.",
                data = new
                {
                    auction.Id,
                    auction.Status
                }
            });
        }

        // TODO: Implement automatic auction completion after the bidding workflow is ready.
        // - Check auctions whose EndTime has passed.
        // - Determine the winner based on the highest valid bid.
        // - Set WinnerId and Result ("Sold" or "NoWinner").
        // - Update Status to "Completed".
        // - Create the Sale record and set the payment deadline.
        // - Ensure the completion process cannot run twice for the same auction.
        // - Coordinate with the bidding and sale/payment workflows.
    }
}