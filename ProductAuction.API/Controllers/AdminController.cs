
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
    }
}
