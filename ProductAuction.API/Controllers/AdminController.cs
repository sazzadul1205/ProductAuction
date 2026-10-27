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
        // Returns aggregated stats for the admin dashboard:
        // user counts, pending moderation items, and the platform wallet balance.
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            // total registered users (excluding the system platform account)
            var totalUsers = await _context.Users
                .CountAsync(u => u.UserName != "platform-system-account");

            // items that need admin review / action
            var pendingProducts = await _context.Products
                .CountAsync(p => p.ApprovalStatus == "PendingApproval");

            var pendingDeposits = await _context.DepositRequests
                .CountAsync(d => d.Status == "PendingVerification");

            var pendingVerifications = await _context.UserProfiles
                .CountAsync(p => p.VerificationStatus == "PendingReview");

            var openDisputes = await _context.Disputes
                .CountAsync(d => d.Status == "Open" || d.Status == "UnderReview");

            // auctions currently running
            var activeAuctions = await _context.Auctions
                .CountAsync(a => a.Status == "Active");

            // grab the platform's system account and its wallet balance
            var platformUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName == "platform-system-account");

            var platformWallet = platformUser != null
                ? await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id)
                : null;

            var platformBalance = platformWallet?.Balance ?? 0;

            return Ok(new
            {
                status = 200,
                data = new
                {
                    totalUsers,
                    pendingProducts,
                    pendingDeposits,
                    pendingVerifications,
                    activeAuctions,
                    openDisputes,
                    platformBalance
                }
            });
        }


        // POST: api/Admin/create-admin
        // Creates a new user account with the Admin role.
        // Also creates the UserProfile and Wallet records so everything stays consistent.
        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminDto dto)
        {
            // basic field checks
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password) || string.IsNullOrEmpty(dto.FullName))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "All fields are required."
                });
            }

            // make sure this email isn't taken yet
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);

            if (existingUser != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Email is already registered."
                });
            }

            // create the Identity account
            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Failed to create admin account.",
                    errors = result.Errors
                });
            }

            // give them the Admin role
            await _userManager.AddToRoleAsync(user, "Admin");

            // create the profile and wallet so the rest of the app has what it expects
            _context.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            });

            _context.Wallets.Add(new Wallet
            {
                UserId = user.Id,
                Balance = 0,
                ReservedBalance = 0
            });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Admin created successfully."
            });
        }


        // GET: api/Admin/user
        // Returns all human users along with their roles and basic profile info.
        [HttpGet("user")]
        public async Task<IActionResult> GetAllUsers()
        {
            // grab registered users, skip the platform system account
            var users = await _userManager.Users
                .Where(u => u.UserName != "platform-system-account")
                .OrderBy(u => u.Email)
                .ToListAsync();

            // load all profiles and wallets once (2 queries total)
            var profiles = await _context.UserProfiles.ToListAsync();
            var wallets = await _context.Wallets.ToListAsync();

            var userList = new List<object>();

            // build the response, one entry per user
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                // look up this user's profile / wallet (may not exist)
                var profile = profiles.FirstOrDefault(p => p.UserId == user.Id);
                var wallet = wallets.FirstOrDefault(w => w.UserId == user.Id);

                // build the profile part of the response (or null)
                object? profileData = null;
                if (profile != null)
                {
                    profileData = new
                    {
                        fullName = profile.FullName,
                        profilePhotoUrl = profile.ProfilePhotoUrl,
                        address = profile.Address,
                        city = profile.City,
                        nationalIdNumber = profile.NationalIdNumber,
                        verificationStatus = profile.VerificationStatus,
                        createdAt = profile.CreatedAt
                    };
                }

                // build the wallet part of the response (or null)
                object? walletData = null;
                if (wallet != null)
                {
                    walletData = new
                    {
                        balance = wallet.Balance,
                        reservedBalance = wallet.ReservedBalance,
                        availableBalance = wallet.Balance - wallet.ReservedBalance
                    };
                }

                userList.Add(new
                {
                    userId = user.Id,
                    email = user.Email,
                    emailConfirmed = user.EmailConfirmed,
                    phoneNumber = user.PhoneNumber,
                    roles,
                    isLocked = user.LockoutEnd > DateTimeOffset.UtcNow,
                    lockoutEnd = user.LockoutEnd,
                    profile = profileData,
                    wallet = walletData
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
        // Locks a user permanently or for a set duration, and also handles unlocking.
        [HttpPut("user/{id}/lock")]
        public async Task<IActionResult> SetUserLock(string id, [FromBody] AdminUserLockDto request)
        {
            var user = await _userManager.FindByIdAsync(id);

            // don't let anyone touch the platform system account
            if (user == null || user.UserName == "platform-system-account")
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User not found."
                });
            }

            // temporary lock needs a positive duration
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

            // permanent lock shouldn't have a duration attached
            if (request.IsLocked && request.IsPermanent && request.DurationMinutes.HasValue)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Do not provide DurationMinutes for a permanent lock."
                });
            }

            // make sure lockout is enabled before we set an end date
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
                    // "forever"
                    lockoutEnd = DateTimeOffset.MaxValue;
                }
                else
                {
                    // lock for the given number of minutes
                    lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes!.Value);
                }
            }

            // passing null here unlocks the account
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

            // clear failed login count when unlocking
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
        // Returns every product for admin management, no matter what status it's in.
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

                    // both approval status and lifecycle status
                    approvalStatus = p.ApprovalStatus,
                    status = p.Status,
                    rejectionReason = p.RejectionReason,
                    condition = p.Condition,
                    createdAt = p.CreatedAt,

                    // grab the main image url 
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
        // Returns all auctions so admins can monitor them.
        [HttpGet("auctions")]
        public async Task<IActionResult> GetAllAuctionsForAdmin()
        {
            var auctions = await _context.Auctions
                .OrderByDescending(a => a.Id)
                .Select(a => new
                {
                    auctionId = a.Id,
                    productId = a.ProductId,

                    // product title 
                    productTitle = _context.Products
                        .Where(p => p.Id == a.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    // main product image 
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
        // Returns finalized auction results so admins can review them.
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

                    // product title (blank if missing)
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

            // can only start review on an open dispute
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
        // Returns all penalty records for admin review.
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
            // only ones still waiting for verification
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
            // find the deposit request
            var depositRequest = await _context.DepositRequests.FirstOrDefaultAsync(d => d.Id == id);

            if (depositRequest == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Deposit request not found."
                });
            }

            // already reviewed? don't credit the wallet twice
            if (depositRequest.Status != "PendingVerification")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This deposit request has already been reviewed."
                });
            }

            // only Approved or Rejected are allowed
            if (dto.Status != "Approved" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Approved or Rejected."
                });
            }

            // rejection needs a reason
            if (dto.Status == "Rejected" &&
                string.IsNullOrEmpty(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Rejection reason is required."
                });
            }

            // find the wallet this deposit belongs to
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == depositRequest.WalletId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // save the review info
            depositRequest.Status = dto.Status;
            depositRequest.ReviewedAt = DateTime.UtcNow;
            depositRequest.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;

            // only approved deposits actually increase the balance
            if (dto.Status == "Approved")
            {
                wallet.Balance += depositRequest.Amount;

                // record the wallet balance change
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


        // GET: api/Admin/transactions
        // Returns every wallet transaction globally, for admin oversight.
        [HttpGet("transactions")]
        public async Task<IActionResult> GetAllTransactions()
        {
            var transactions = await _context.WalletTransactions
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.Type,
                    t.Amount,
                    t.BalanceAfter,
                    t.Reference,
                    t.CreatedAt,

                    // grab the user's full name 
                    UserName = _context.UserProfiles
                        .Where(p => p.UserId == _context.Wallets
                            .Where(w => w.Id == t.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown",

                    UserEmail = _context.Users
                        .Where(u => u.Id == _context.Wallets
                            .Where(w => w.Id == t.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(u => u.Email)
                        .FirstOrDefault() ?? "Unknown"
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Transactions retrieved successfully.",
                data = transactions
            });
        }


        // GET: api/Admin/reservations
        // Returns all wallet reservations globally, for admin oversight.
        [HttpGet("reservations")]
        public async Task<IActionResult> GetAllReservations()
        {
            var reservations = await _context.WalletReservations
                .OrderByDescending(r => r.Id)
                .Select(r => new
                {
                    r.Id,
                    r.Type,
                    r.Amount,
                    r.Status,
                    r.ProductId,
                    r.AuctionId,
                    r.SaleId,

                    // grab the user's full name
                    UserName = _context.UserProfiles
                        .Where(p => p.UserId == _context.Wallets
                            .Where(w => w.Id == r.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown",

                    // product title, only if this reservation is tied to a product
                    ProductTitle = r.ProductId != null
                        ? _context.Products
                            .Where(p => p.Id == r.ProductId)
                            .Select(p => p.Title)
                            .FirstOrDefault()
                        : null
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Reservations retrieved successfully.",
                data = reservations
            });
        }


        // GET: api/Admin/platform-wallet
        // Returns the platform system account's wallet and its transaction history.
        [HttpGet("platform-wallet")]
        public async Task<IActionResult> GetPlatformWallet()
        {
            var platformUser = await _userManager.FindByNameAsync("platform-system-account");

            if (platformUser == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Platform account not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Platform wallet not found."
                });
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