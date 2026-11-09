using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Admin;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Wallet;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _context;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            ILogger<AdminController> logger)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }


        // GET: api/Admin/dashboard
        // Returns aggregated admin dashboard stats.
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            // Total users
            var totalUsers = await _context.Users.CountAsync(u => u.UserName != "platform-system-account");

            // Pending items.
            var pendingProducts = await _context.Products.CountAsync(p => p.ApprovalStatus == ProductApprovalStatus.PendingApproval);
            var pendingDeposits = await _context.DepositRequests.CountAsync(d => d.Status == DepositRequestStatus.PendingVerification);
            var pendingVerifications = await _context.UserProfiles.CountAsync(p => p.VerificationStatus == VerificationStatus.PendingReview);
            var openDisputes = await _context.Disputes.CountAsync(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview);

            // Active.
            var activeAuctions = await _context.Auctions.CountAsync(a => a.Status == AuctionStatus.Active);

            // Platform wallet balance
            var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");
            var platformWallet = platformUser != null
                ? await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id)
                : null;
            var platformBalance = platformWallet?.Balance;

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
        // Creates a new user account with an elevated role (Admin or a custom role).
        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminDto dto)
        {
            // Basic field checks.
            if (string.IsNullOrEmpty(dto.Email)
                || string.IsNullOrEmpty(dto.Password)
                || string.IsNullOrEmpty(dto.FullName)
                || string.IsNullOrEmpty(dto.Role))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "All fields are required."
                });
            }

            // Role must exist and must not be Seller or Buyer.
            var role = dto.Role.Trim();

            if (role.Equals("Seller", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("Buyer", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Seller and Buyer roles cannot be assigned here. Use the public registration flow for those."
                });
            }

            var roleExists = await _context.Roles.AnyAsync(r => r.Name == role);
            if (!roleExists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Role '{role}' does not exist."
                });
            }

            // Ensure the email isn't already taken.
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Email is already registered."
                });
            }

            // Create the Identity account.
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
                    message = "Failed to create account.",
                    errors = result.Errors
                });
            }

            // Assign the requested role.
            var roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                // Roll back the user so we don't leave an orphan account.
                await _userManager.DeleteAsync(user);
                return BadRequest(new
                {
                    status = 400,
                    message = "Failed to assign role.",
                    errors = roleResult.Errors
                });
            }

            _context.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = $"Account created successfully with role '{role}'.",
                data = new
                {
                    userId = user.Id,
                    email = user.Email,
                    role
                }
            });
        }

        // PUT: api/Admin/user/{id}
        // Edit a staff account: name, email, and role.
        // Seller and Buyer accounts cannot be edited here.
        [HttpPut("user/{id}")]
        public async Task<IActionResult> EditUser(string id, [FromBody] AdminEditUserDto dto)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound(new { status = 404, message = "User not found." });

            var currentRoles = await _userManager.GetRolesAsync(user);
            var currentRole = currentRoles.FirstOrDefault() ?? "";

            // Block editing Seller / Buyer accounts
            if (currentRole.Equals("Seller", StringComparison.OrdinalIgnoreCase) ||
                currentRole.Equals("Buyer", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Seller and Buyer accounts cannot be edited from the admin panel."
                });
            }

            // Validate requested new role.
            var newRole = dto.Role.Trim();

            if (newRole.Equals("Seller", StringComparison.OrdinalIgnoreCase) ||
                newRole.Equals("Buyer", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Seller and Buyer roles cannot be assigned here."
                });
            }

            if (!await _context.Roles.AnyAsync(r => r.Name == newRole))
                return BadRequest(new { status = 400, message = $"Role '{newRole}' does not exist." });

            // Email change.
            var email = dto.Email.Trim();

            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var taken = await _userManager.FindByEmailAsync(email);
                if (taken != null && taken.Id != user.Id)
                    return Conflict(new { status = 409, message = "Email is already used by another account." });

                await _userManager.SetEmailAsync(user, email);
                await _userManager.SetUserNameAsync(user, email);
            }

            // Profile name.
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
            {
                profile = new UserProfile { UserId = user.Id, CreatedAt = DateTime.UtcNow };
                _context.UserProfiles.Add(profile);
            }

            profile.FullName = dto.FullName.Trim();
            profile.UpdatedAt = DateTime.UtcNow;

            // Role change.
            if (!string.Equals(currentRole, newRole, StringComparison.OrdinalIgnoreCase))
            {
                // Block demoting the only remaining Admin.
                if (currentRole == "Admin" && newRole != "Admin")
                {
                    var admins = await _userManager.GetUsersInRoleAsync("Admin");
                    if (admins.Count <= 1)
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Cannot change the role of the last active administrator."
                        });
                }

                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, newRole);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "User updated successfully.",
                data = new
                {
                    userId = user.Id,
                    email = user.Email,
                    roles = await _userManager.GetRolesAsync(user)
                }
            });
        }


        // PUT: api/Admin/user/{id}/reset-password
        // Admin/Staff can reset a user's password directly (no current password/confirmation flow).
        [HttpPut("user/{id}/reset-password")]
        [Authorize] // allow any authenticated user; staff/admin check enforced below
        public async Task<IActionResult> ResetUserPassword(string id, [FromBody] AdminResetPasswordDto dto)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User not found."
                });
            }
            if (user.UserName == "platform-system-account")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This account cannot be modified."
                });
            }

            var targetRoles = await _userManager.GetRolesAsync(user);
            if (targetRoles.Contains("Buyer") || targetRoles.Contains("Seller"))
            {
                return
                BadRequest(new
                {
                    status = 400,
                    message = "Passwords for Buyer/Seller accounts cannot be reset here."
                });
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Failed to reset password.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Password reset successfully."
            });
        }

        // GET: api/Admin/user
        // Returns all human users along with their roles and basic profile info.
        [HttpGet("user")]
        public async Task<IActionResult> GetAllUsers([FromQuery] PaginationQueryDto query)
        {
            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // role filter: Admin / Seller / Buyer
            var filter = query.Filter;

            // grab registered users
            var userQuery = _userManager.Users
                .Where(u => u.UserName != "platform-system-account")
                .AsQueryable();

            // apply role filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                var roleUserIds = await _context.UserRoles
                    .Where(ur => _context.Roles
                        .Where(r => r.Name == filter)
                        .Select(r => r.Id)
                        .Contains(ur.RoleId))
                    .Select(ur => ur.UserId)
                    .ToListAsync();

                userQuery = userQuery.Where(u => roleUserIds.Contains(u.Id));
            }

            // apply search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProfileUserIds = await _context.UserProfiles
                    .Where(p => p.FullName.Contains(term) || (p.City != null && p.City.Contains(term)))
                    .Select(p => p.UserId)
                    .ToListAsync();

                userQuery = userQuery.Where(u =>
                    (u.Email != null && u.Email.Contains(term)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(term)) ||
                    matchingProfileUserIds.Contains(u.Id));
            }

            userQuery = userQuery.OrderByDescending(u => u.Id);

            var totalCount = await userQuery.CountAsync();

            var users = await userQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // load all profiles and wallets
            var profiles = await _context.UserProfiles.ToListAsync();
            var wallets = await _context.Wallets.ToListAsync();

            var userList = new List<object>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                //  users profile / wallet
                var profile = profiles.FirstOrDefault(p => p.UserId == user.Id);
                var wallet = wallets.FirstOrDefault(w => w.UserId == user.Id);

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
                        verificationStatus = profile.VerificationStatus.ToString(),
                        createdAt = profile.CreatedAt
                    };
                }

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
                data = new
                {
                    items = userList,
                    page,
                    pageSize,
                    totalCount,
                }
            });
        }


        // PUT: api/Admin/user/{id}/lock
        // Locks a user permanently or for a set duration, and also handles unlocking.
        [HttpPut("user/{id}/lock")]
        public async Task<IActionResult> SetUserLock(string id, [FromBody] AdminUserLockDto request)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
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

            // Set the Lockout Value as Null
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


        // GET: api/Admin/auctions
        // Returns all auctions so admins can monitor them.
        [HttpGet("auctions")]
        public async Task<IActionResult> GetAllAuctionsForAdmin([FromQuery] PaginationQueryDto query)
        {
            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // status filter: Upcoming / Active / Completed / Cancelled.
            var filter = query.Filter;

            var auctionsQuery = _context.Auctions.AsQueryable();

            // apply status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "NeedsSchedule")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == AuctionStatus.Upcoming && a.StartTime == null);
                }
                else if (filter == "Scheduled")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == AuctionStatus.Upcoming && a.StartTime != null);
                }
                else if (filter == "History")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == AuctionStatus.Completed || a.Status == AuctionStatus.Cancelled);
                }
                else if (Enum.TryParse<AuctionStatus>(filter, ignoreCase: true, out var parsedStatus))
                {
                    auctionsQuery = auctionsQuery.Where(a => a.Status == parsedStatus);
                }
            }

            // apply search filter 
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                auctionsQuery = auctionsQuery.Where(a =>
                    matchingProductIds.Contains(a.ProductId));
            }

            auctionsQuery = auctionsQuery.OrderByDescending(a => a.Id);

            var totalCount = await auctionsQuery.CountAsync();

            var auctions = await auctionsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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
                    sellerName = _context.UserProfiles
                        .Where(p => p.UserId == a.SellerId)
                        .Select(p => p.FullName)
                        .FirstOrDefault(),
                    startTime = a.StartTime,
                    endTime = a.EndTime,
                    status = a.Status.ToString(),
                    currentHighestBid = a.CurrentHighestBid,
                    currentHighestBidderId = a.CurrentHighestBidderId,
                    bidCount = a.BidCount,
                    winnerId = a.WinnerId,
                    winnerName = _context.UserProfiles
                        .Where(p => p.UserId == a.WinnerId)
                        .Select(p => p.FullName)
                        .FirstOrDefault(),
                    result = a.Result != null ? a.Result.ToString() : null,
                    cancellationReason = a.CancellationReason
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "All auctions retrieved successfully.",
                data = new
                {
                    items = auctions,
                    page,
                    pageSize,
                    totalCount
                }
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
            if (dispute.Status != DisputeStatus.Open)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Only Open disputes can start review. Current status: {dispute.Status}."
                });
            }

            dispute.Status = DisputeStatus.UnderReview;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Dispute review started successfully.",
                data = new
                {
                    disputeId = dispute.Id,
                    Status = dispute.Status.ToString()
                }
            });
        }

        // GET: api/wallet/deposit-requests/pending
        // Returns all deposit requests waiting for admin review.
        [HttpGet("deposit-requests/pending")]
        public async Task<IActionResult> GetPendingDepositRequests()
        {
            var requests = await _context.DepositRequests
                .Where(d => d.Status == DepositRequestStatus.PendingVerification)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DepositRequestResponseDto
                {
                    Id = d.Id,
                    WalletId = d.WalletId,
                    Method = d.Method,
                    Amount = d.Amount,
                    TransactionReference = d.TransactionReference,
                    ReceiptImageUrl = d.ReceiptImageUrl,
                    Status = d.Status.ToString(),
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
            var depositRequest = await _context.DepositRequests.FirstOrDefaultAsync(d => d.Id == id);

            if (depositRequest == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Deposit request not found."
                });
            }

            // already reviewed
            if (depositRequest.Status != DepositRequestStatus.PendingVerification)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This deposit request has already been reviewed."
                });
            }

            // rejection needs a reason
            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
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
            depositRequest.Status = dto.Status == "Approved"
                ? DepositRequestStatus.Approved
                : DepositRequestStatus.Rejected;

            depositRequest.ReviewedAt = DateTime.UtcNow;
            depositRequest.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;

            // only approved deposits actually increase the balance
            if (dto.Status == "Approved")
            {
                // Increment Deposit Request Amount + Previous Wallet Ballance
                wallet.Balance += depositRequest.Amount;

                // record the wallet balance change
                var transaction = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = WalletTransactionType.Credit,
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
                    Status = depositRequest.Status.ToString(),
                    depositRequest.RejectionReason,
                    wallet.Balance
                }
            });
        }


        // GET: api/Admin/transactions
        // Returns every wallet transaction globally
        [HttpGet("transactions")]
        public async Task<IActionResult> GetAllTransactions([FromQuery] PaginationQueryDto query)
        {
            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // transaction type filter: Credit / Debit / Penalty / Release / Commission
            var filter = query.Filter;

            var queryable = _context.WalletTransactions.AsQueryable();

            // apply type filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All"
                && Enum.TryParse<WalletTransactionType>(filter, ignoreCase: true, out var parsedType))
            {
                queryable = queryable.Where(wt => wt.Type == parsedType);
            }

            // apply search
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                // user ids whose profile name matches
                var nameMatchUserIds = await _context.UserProfiles
                    .Where(p => p.FullName.Contains(term))
                    .Select(p => p.UserId)
                    .ToListAsync();

                // user ids whose email matches
                var emailMatchUserIds = await _context.Users
                    .Where(u => u.Email != null && u.Email.Contains(term))
                    .Select(u => u.Id)
                    .ToListAsync();

                // wallets owned by those users
                var matchingWalletIds = await _context.Wallets
                    .Where(w => nameMatchUserIds.Contains(w.UserId) || emailMatchUserIds.Contains(w.UserId))
                    .Select(w => w.Id)
                    .ToListAsync();

                queryable = queryable.Where(wt =>
                    (wt.Reference != null && wt.Reference.Contains(term)) ||
                    matchingWalletIds.Contains(wt.WalletId));
            }

            queryable = queryable.OrderByDescending(wt => wt.CreatedAt);

            var totalCount = await queryable.CountAsync();

            // Global stats.

            // Total volume
            var totalVolume = await _context.WalletTransactions
                .Where(t => t.Type == WalletTransactionType.Credit)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            // Escrow currently held
            var totalEscrow = await _context.WalletReservations
                .Where(r => r.Type == ReservationType.SaleEscrow && r.Status == ReservationStatus.Reserved)
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            // Active buyer eligibility deposits.
            var totalBuyerDeposits = await _context.WalletReservations
                .Where(r => r.Type == ReservationType.BuyerFunds && r.Status == ReservationStatus.Reserved)
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            // Active seller deposits.
            var totalSellerDeposits = await _context.WalletReservations
                .Where(r => r.Type == ReservationType.SellerDeposit && r.Status == ReservationStatus.Reserved)
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            // Count of active reservations
            var totalActiveReservations = await _context.WalletReservations.CountAsync(r => r.Status == ReservationStatus.Reserved);


            var transactions = await queryable
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(wt => new
                {
                    wt.Id,
                    Type = wt.Type.ToString(),
                    wt.Amount,
                    wt.BalanceAfter,
                    wt.Reference,
                    wt.CreatedAt,

                    // Get the user's full name using the wallet's UserId.
                    UserName = _context.UserProfiles
                        .Where(up => up.UserId == _context.Wallets
                            .Where(w => w.Id == wt.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(up => up.FullName)
                        .FirstOrDefault(),

                    // Get the user's email using the wallet's UserId.
                    UserEmail = _context.Users
                        .Where(u => u.Id == _context.Wallets
                            .Where(w => w.Id == wt.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(u => u.Email)
                        .FirstOrDefault()
                })
                .ToListAsync();


            return Ok(new
            {
                status = 200,
                message = "Transactions retrieved successfully.",
                data = new
                {
                    items = transactions,
                    page,
                    pageSize,
                    totalCount,
                    totalVolume,
                    totalEscrow,
                    totalBuyerDeposits,
                    totalSellerDeposits,
                    totalActiveReservations
                }
            });
        }

        // GET: api/Admin/reservations
        // Returns all wallet reservations globally
        [HttpGet("reservations")]
        public async Task<IActionResult> GetAllReservations([FromQuery] PaginationQueryDto query)
        {
            var page = query.Page;
            if (page < 1) page = 1;
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var search = query.Search;

            var queryable = _context.WalletReservations
                .Where(r => r.Status == ReservationStatus.Reserved)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                var nameMatchUserIds = await _context.UserProfiles
                    .Where(p => p.FullName.ToLower().Contains(term))
                    .Select(p => p.UserId)
                    .ToListAsync();
                var matchingWalletIds = await _context.Wallets
                    .Where(w => nameMatchUserIds.Contains(w.UserId))
                    .Select(w => w.Id)
                    .ToListAsync();
                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.ToLower().Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                queryable = queryable.Where(r =>
                    matchingWalletIds.Contains(r.WalletId) ||
                    (r.ProductId.HasValue && matchingProductIds.Contains(r.ProductId.Value)) ||
                    r.Type.ToString().ToLower().Contains(term));
            }

            var totalCount = await queryable.CountAsync();

            var reservations = await queryable
                .OrderByDescending(wr => wr.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(wr => new
                {
                    wr.Id,
                    Type = wr.Type.ToString(),
                    wr.Amount,
                    Status = wr.Status.ToString(),
                    wr.ProductId,
                    wr.AuctionId,
                    wr.SaleId,

                    UserName = _context.UserProfiles
                        .Where(up => up.UserId == _context.Wallets
                            .Where(w => w.Id == wr.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(p => p.FullName)
                        .FirstOrDefault(),

                    ProductTitle = wr.ProductId != null
                        ? _context.Products
                            .Where(p => p.Id == wr.ProductId)
                            .Select(p => p.Title)
                            .FirstOrDefault()
                        : null
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Reservations retrieved successfully.",
                data = reservations,
                page,
                pageSize,
                totalCount
            });
        }


        // GET: api/Admin/platform-wallet
        // Returns the platform system account's wallet and its transaction history.
        [HttpGet("platform-wallet")]
        public async Task<IActionResult> GetPlatformWallet([FromQuery] PaginationQueryDto query)
        {
            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional transaction-type filter
            var filter = query.Filter;

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

            var txQuery = _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .AsQueryable();

            // Total balances held by sellers and buyers (exclude platform)
            var platformWalletId = wallet.Id;
            var sellerRoleId = await _context.Roles.Where(r => r.Name == "Seller").Select(r => r.Id).FirstOrDefaultAsync();
            var buyerRoleId = await _context.Roles.Where(r => r.Name == "Buyer").Select(r => r.Id).FirstOrDefaultAsync();

            var sellerUserIds = _context.UserRoles.Where(ur => ur.RoleId == sellerRoleId).Select(ur => ur.UserId);
            var buyerUserIds = _context.UserRoles.Where(ur => ur.RoleId == buyerRoleId).Select(ur => ur.UserId);

            var sellerTotals = await _context.Wallets
                .Where(w => w.Id != platformWalletId && sellerUserIds.Contains(w.UserId))
                .GroupBy(_ => 1)
                .Select(g => new { Balance = g.Sum(w => w.Balance), Reserved = g.Sum(w => w.ReservedBalance) })
                .FirstOrDefaultAsync();

            var buyerTotals = await _context.Wallets
                .Where(w => w.Id != platformWalletId && buyerUserIds.Contains(w.UserId))
                .GroupBy(_ => 1)
                .Select(g => new { Balance = g.Sum(w => w.Balance), Reserved = g.Sum(w => w.ReservedBalance) })
                .FirstOrDefaultAsync();

            // apply type filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All"
                && Enum.TryParse<WalletTransactionType>(filter, ignoreCase: true, out var parsedType))
            {
                txQuery = txQuery.Where(t => t.Type == parsedType);
            }

            // apply search filter 
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                txQuery = txQuery.Where(t =>
                    (t.Reference != null && t.Reference.Contains(term)) ||
                    t.Type.ToString().Contains(term));
            }

            txQuery = txQuery.OrderByDescending(t => t.CreatedAt);

            var totalCount = await txQuery.CountAsync();

            var transactions = await txQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new WalletTransactionResponseDto
                {
                    Id = t.Id,
                    Type = t.Type.ToString(),
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
                    sellerBalances = new
                    {
                        balance = sellerTotals?.Balance ?? 0m,
                        reservedBalance = sellerTotals?.Reserved ?? 0m,
                        total = (sellerTotals?.Balance ?? 0m) + (sellerTotals?.Reserved ?? 0m)
                    },
                    buyerBalances = new
                    {
                        balance = buyerTotals?.Balance ?? 0m,
                        reservedBalance = buyerTotals?.Reserved ?? 0m,
                        total = (buyerTotals?.Balance ?? 0m) + (buyerTotals?.Reserved ?? 0m)
                    },
                    transactions = new
                    {
                        items = transactions,
                        page,
                        pageSize,
                        totalCount
                    }
                }
            });
        }


        // GET: api/Admin/user/role-counts
        // Returns global counts of users per role
        [HttpGet("user/role-counts")]
        public async Task<IActionResult> GetUserRoleCounts()
        {
            // Get the IDs of every user, excluding the platform system account.
            var allUserIds = await _userManager.Users
                .Where(u => u.UserName != "platform-system-account")
                .Select(u => u.Id)
                .ToListAsync();

            // Load the role rows for those users.
            var userRoleRows = await _context.UserRoles
                .Where(ur => allUserIds.Contains(ur.UserId))
                .ToListAsync();

            // Load all roles
            var allRoles = await _context.Roles.ToListAsync();

            // Count roles: Admin + any other non-Buyer/Seller role = staff
            var adminCount = 0;
            var staffCount = 0;
            var sellerCount = 0;
            var buyerCount = 0;

            foreach (var userRole in userRoleRows)
            {
                var role = allRoles.FirstOrDefault(r => r.Id == userRole.RoleId);
                if (role == null) continue;

                if (role.Name == "Admin") { adminCount++; staffCount++; }
                else if (role.Name == "Seller") sellerCount++;
                else if (role.Name == "Buyer") buyerCount++;
                else staffCount++;
            }

            return Ok(new
            {
                status = 200,
                message = "Role counts retrieved successfully.",
                data = new
                {
                    admin = adminCount,
                    staff = staffCount,
                    seller = sellerCount,
                    buyer = buyerCount
                }
            });
        }
    }
}