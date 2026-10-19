
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Admin;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _context;

        public AdminController(
            UserManager<IdentityUser> userManager,
            AppDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        // GET: api/Admin
        // Retrieves all human users with their roles and basic profiles.
        [HttpGet("user")]
        public async Task<IActionResult> GetAllUsers()
        {
            // Retrieve registered users, excluding the platform system account.
            var users = await _userManager.Users
                .Where(u => u.UserName != "platform-system-account")
                .OrderBy(u => u.Email)
                .ToListAsync();

            // Retrieve profiles for these users in one database query.
            var userIds = users.Select(u => u.Id).ToList();

            var profiles = await _context.UserProfiles
                .Where(p => userIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId);

            var userList = new List<object>();

            // Include the roles assigned to each user.
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                profiles.TryGetValue(user.Id, out var profile);

                userList.Add(new
                {
                    userId = user.Id,
                    email = user.Email,
                    emailConfirmed = user.EmailConfirmed,
                    phoneNumber = user.PhoneNumber,
                    roles,

                    profile = profile == null ? null : new
                    {
                        fullName = profile.FullName,
                        profilePhotoUrl = profile.ProfilePhotoUrl,
                        city = profile.City,
                        verificationStatus = profile.VerificationStatus,
                        createdAt = profile.CreatedAt
                    }
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Users retrieved successfully.",
                data = userList
            });
        }

        // PUT: api/Admin/user/{id}/lock
        // Locks a user permanently or for a specified duration.
        // Also allows an admin to unlock the account.
        [HttpPut("user/{id}/lock")]
        public async Task<IActionResult> SetUserLock(string id, [FromBody] AdminUserLockDto request)
        {
            var user = await _userManager.FindByIdAsync(id);

            // Do not expose or modify the platform system account.
            if (user == null || user.UserName == "platform-system-account")
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User not found."
                });
            }

            if (request.IsLocked && !request.IsPermanent)
            {
                if (!request.DurationMinutes.HasValue || request.DurationMinutes.Value <= 0)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "DurationMinutes must be greater than zero for a temporary lock."
                    });
                }
            }

            if (request.IsLocked &&
                request.IsPermanent &&
                request.DurationMinutes.HasValue)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Do not provide DurationMinutes for a permanent lock."
                });
            }

            var enableLockout = await _userManager.SetLockoutEnabledAsync(user, true);

            if (!enableLockout.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Could not enable account lockout.",
                    errors = enableLockout.Errors.Select(e => e.Description)
                });
            }

            DateTimeOffset? lockoutEnd = null;

            if (request.IsLocked)
            {
                if (request.IsPermanent)
                {
                    lockoutEnd = DateTimeOffset.MaxValue;
                }
                else
                {
                    lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes!.Value);
                }
            }

            // Setting null unlocks the account.
            var result = await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Failed to update the user lockout.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            if (!request.IsLocked)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
            }

            return Ok(new
            {
                status = 200,
                message = !request.IsLocked
                    ? "User account unlocked successfully."
                    : request.IsPermanent
                        ? "User account permanently locked."
                        : "User account temporarily locked.",
                data = new
                {
                    userId = user.Id,
                    email = user.Email,
                    isLocked = request.IsLocked,
                    isPermanent = request.IsLocked && request.IsPermanent,
                    lockoutEnd
                }
            });
        }


        // GET: api/Admin/product
        // Retrieves all products for admin management, regardless of status.
        [HttpGet("product")]
        public async Task<IActionResult> GetAllProductsForAdmin()
        {
            var products = await _context.Products
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    id = p.Id,
                    sellerId = p.SellerId,
                    categoryId = p.CategoryId,
                    categoryName = p.Category.Name,
                    title = p.Title,
                    shortSummary = p.ShortSummary,
                    startingPrice = p.StartingPrice,
                    buyoutPrice = p.BuyoutPrice,

                    // Include both approval and product lifecycle statuses.
                    approvalStatus = p.ApprovalStatus,
                    status = p.Status,
                    rejectionReason = p.RejectionReason,
                    createdAt = p.CreatedAt,

                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == p.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "All products retrieved successfully.",
                data = products
            });
        }



        // GET: api/Admin/auctions
        // Retrieves all auctions for administrative monitoring.
        [HttpGet("auctions")]
        public async Task<IActionResult> GetAllAuctionsForAdmin()
        {
            var auctions = await _context.Auctions
                .OrderByDescending(a => a.Id)
                .Select(a => new
                {
                    auctionId = a.Id,
                    productId = a.ProductId,

                    productTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    sellerId = a.SellerId,
                    startTime = a.StartTime,
                    endTime = a.EndTime,
                    status = a.Status,
                    currentHighestBid = a.CurrentHighestBid,
                    currentHighestBidderId = a.CurrentHighestBidderId,
                    bidCount = a.BidCount,
                    winnerId = a.WinnerId,
                    result = a.Result,
                    cancellationReason = a.CancellationReason
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "All auctions retrieved successfully.",
                data = auctions
            });
        }


        // GET: api/Admin/auction-results
        // Retrieves finalized auction results for administrative review.
        [HttpGet("auction-results")]
        public async Task<IActionResult> GetAuctionResultsForAdmin()
        {
            var results = await _context.Auctions
                .Where(a =>
                    a.Status == "Completed" &&
                    (a.Result == "Sold" || a.Result == "NoWinner"))
                .OrderByDescending(a => a.EndTime)
                .Select(a => new
                {
                    auctionId = a.Id,
                    productId = a.ProductId,

                    productTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    sellerId = a.SellerId,
                    winnerId = a.WinnerId,
                    result = a.Result,
                    finalAmount = a.CurrentHighestBid,
                    bidCount = a.BidCount,
                    startTime = a.StartTime,
                    endTime = a.EndTime
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Auction results retrieved successfully.",
                data = results
            });
        }


        // POST: api/Admin/disputes/{id}/start-review
        // Moves an open dispute into the admin review process.
        [HttpPost("disputes/{id}/start-review")]
        public async Task<IActionResult> StartDisputeReview(int id)
        {
            var dispute = await _context.Disputes.FirstOrDefaultAsync(d => d.Id == id);

            if (dispute == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Dispute not found."
                });
            }

            // Only open disputes can begin review.
            if (dispute.Status != "Open")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Only Open disputes can start review. Current status: {dispute.Status}."
                });
            }

            dispute.Status = "UnderReview";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Dispute review started successfully.",
                data = new
                {
                    disputeId = dispute.Id,
                    dispute.Status
                }
            });
        }

        // GET: api/Admin/penalties
        // Retrieves all penalty records for administrative review.
        [HttpGet("penalties")]
        public async Task<IActionResult> GetAllPenalties()
        {
            var penalties = await _context.Penalties
                .OrderByDescending(p => p.Id)
                .Select(p => new
                {
                    id = p.Id,
                    disputeId = p.DisputeId,
                    againstUserId = p.AgainstUserId,
                    productValue = p.ProductValue,
                    totalAmount = p.TotalAmount,
                    oppositePartyAmount = p.OppositePartyAmount,
                    platformAmount = p.PlatformAmount,
                    status = p.Status
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Penalties retrieved successfully.",
                data = penalties
            });
        }

    }
}
