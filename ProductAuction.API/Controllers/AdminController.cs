using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Admin;
using ProductAuction.API.DTOs.Wallet;
using ProductAuction.API.Models;

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

        // GET: api/Admin/dashboard
        // Retrieves aggregated statistics for the admin dashboard,
        // including user counts, pending moderation items, and platform wallet balance.
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            // Count all registered users, excluding the system platform account.
            var totalUsers = await _context.Users.CountAsync(u => u.UserName != "platform-system-account");

            // Count items currently requiring admin review or action.
            var pendingProducts = await _context.Products.CountAsync(p => p.ApprovalStatus == "PendingApproval");
            var pendingDeposits = await _context.DepositRequests.CountAsync(d => d.Status == "PendingVerification");
            var pendingVerifications = await _context.UserProfiles.CountAsync(p => p.VerificationStatus == "PendingReview");
            var openDisputes = await _context.Disputes.CountAsync(d => d.Status == "Open" || d.Status == "UnderReview");

            // Count currently active auctions.
            var activeAuctions = await _context.Auctions.CountAsync(a => a.Status == "Active");

            // Retrieve the platform's system account and its current wallet balance.
            var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");
            var platformWallet = platformUser != null ? await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id) : null;
            var platformBalance = platformWallet?.Balance ?? 0;

            return Ok(new
            {
                status = 200,
                data = new { totalUsers, pendingProducts, pendingDeposits, pendingVerifications, activeAuctions, openDisputes, platformBalance }
            });
        }

        // POST: api/Admin/create-admin
        // Creates a new user account with the Admin role.
        // Also generates the required UserProfile and Wallet records to maintain data integrity.
        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminDto dto)
        {
            // Validate required fields.
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password) || string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(new { status = 400, message = "All fields are required." });

            // Ensure the email is not already registered.
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null)
                return BadRequest(new { status = 400, message = "Email is already registered." });

            // Start a transaction so all database changes succeed or roll back together.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Create the Identity account.
                var user = new IdentityUser { UserName = dto.Email, Email = dto.Email, EmailConfirmed = true };
                var result = await _userManager.CreateAsync(user, dto.Password);

                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { status = 400, message = "Failed to create admin account.", errors = result.Errors });
                }

                // 2. Assign the Admin role.
                await _userManager.AddToRoleAsync(user, "Admin");

                // 3. Create the UserProfile and Wallet (to maintain DB integrity).
                _context.UserProfiles.Add(new UserProfile { UserId = user.Id, FullName = dto.FullName, CreatedAt = DateTime.UtcNow });
                _context.Wallets.Add(new Wallet { UserId = user.Id, Balance = 0, ReservedBalance = 0 });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { status = 200, message = "Admin created successfully." });
            }
            catch (Exception)
            {
                // Undo the database changes if an unexpected error occurs.
                await transaction.RollbackAsync();
                return StatusCode(500, new { status = 500, message = "An error occurred while creating the admin." });
            }
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
                    condition = p.Condition,
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

                    mainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == a.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",

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


        // GET: api/wallet/deposit-requests
        // Returns all deposit requests for admin history.
        [HttpGet("deposit-requests")]
        public async Task<IActionResult> GetAllDepositRequests()
        {
            var requests = await _context.DepositRequests
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DepositRequestResponseDto
                {
                    Id = d.Id,
                    WalletId = d.WalletId,
                    Method = d.Method,
                    Amount = d.Amount,
                    TransactionReference = d.TransactionReference,
                    ReceiptImageUrl = d.ReceiptImageUrl,
                    Status = d.Status,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Deposit request history retrieved successfully.",
                data = requests
            });
        }



        // GET: api/wallet/deposit-requests/pending
        // Returns all deposit requests waiting for admin review.
        [HttpGet("deposit-requests/pending")]
        public async Task<IActionResult> GetPendingDepositRequests()
        {
            // Get deposit requests that are still awaiting verification.
            var requests = await _context.DepositRequests
                .Where(d => d.Status == "PendingVerification")
                .OrderBy(d => d.CreatedAt)
                .Select(d => new DepositRequestResponseDto
                {
                    Id = d.Id,
                    WalletId = d.WalletId,
                    Method = d.Method,
                    Amount = d.Amount,
                    TransactionReference = d.TransactionReference,
                    ReceiptImageUrl = d.ReceiptImageUrl,
                    Status = d.Status,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Pending deposit requests retrieved successfully.",
                data = requests
            });
        }

        // PUT: api/wallet/deposit-requests/{id}/review
        // Approves or rejects a deposit request.
        [HttpPut("deposit-requests/{id}/review")]
        public async Task<IActionResult> ReviewDepositRequest(int id, [FromBody] ReviewDepositRequestDto dto)
        {
            // Find the deposit request.
            var depositRequest = await _context.DepositRequests.FirstOrDefaultAsync(d => d.Id == id);

            if (depositRequest == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Deposit request not found."
                });
            }

            // Prevent reviewing the same deposit request more than once to avoid crediting the wallet multiple times.
            if (depositRequest.Status != "PendingVerification")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This deposit request has already been reviewed."
                });
            }

            // Only Approved or Rejected statuses are allowed.
            if (dto.Status != "Approved" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Approved or Rejected."
                });
            }

            // A rejection must include a reason.
            if (dto.Status == "Rejected" &&
                string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Rejection reason is required."
                });
            }

            // Find the wallet associated with the deposit request.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == depositRequest.WalletId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Update the review information.
            depositRequest.Status = dto.Status;
            depositRequest.ReviewedAt = DateTime.UtcNow;
            depositRequest.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;

            // Only approved deposits increase the wallet balance.
            if (dto.Status == "Approved")
            {
                wallet.Balance += depositRequest.Amount;

                // Record the wallet balance change.
                var transaction = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = "Credit",
                    Amount = depositRequest.Amount,
                    BalanceAfter = wallet.Balance,
                    Reference = $"DepositRequest-{depositRequest.Id}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.WalletTransactions.Add(transaction);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = dto.Status == "Approved"
                    ? "Deposit request approved successfully."
                    : "Deposit request rejected successfully.",
                data = new
                {
                    depositRequest.Id,
                    depositRequest.Status,
                    depositRequest.RejectionReason,
                    wallet.Balance
                }
            });
        }

        // GET: api/admin/platform-wallet
        // Returns the platform system account wallet and its transaction history.
        [HttpGet("platform-wallet")]
        public async Task<IActionResult> GetPlatformWallet()
        {
            var platformUser = await _userManager.FindByNameAsync("platform-system-account");
            if (platformUser == null)
            {
                return NotFound(new { status = 404, message = "Platform account not found." });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id);
            if (wallet == null)
            {
                return NotFound(new { status = 404, message = "Platform wallet not found." });
            }

            var transactions = await _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new WalletTransactionResponseDto
                {
                    Id = t.Id,
                    Type = t.Type,
                    Amount = t.Amount,
                    BalanceAfter = t.BalanceAfter,
                    Reference = t.Reference,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Platform wallet retrieved successfully.",
                data = new
                {
                    wallet = new WalletResponseDto
                    {
                        Id = wallet.Id,
                        Balance = wallet.Balance,
                        ReservedBalance = wallet.ReservedBalance
                    },
                    transactions
                }
            });
        }

    }
}