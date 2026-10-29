using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Admin;
using ProductAuction.API.DTOs.Common;
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
            var totalUsers = await _context.Users.CountAsync(u => u.UserName != "platform-system-account");

            // items that need admin review / action
            // Pending Products
            var pendingProducts = await _context.Products.CountAsync(p => p.ApprovalStatus == "PendingApproval");

            //pending Deposit Varification
            var pendingDeposits = await _context.DepositRequests.CountAsync(d => d.Status == "PendingVerification");

            // pending User Profile Verifications
            var pendingVerifications = await _context.UserProfiles.CountAsync(p => p.VerificationStatus == "PendingReview");

            // Pending Dispute Resolve
            var openDisputes = await _context.Disputes.CountAsync(d => d.Status == "Open" || d.Status == "UnderReview");

            // auctions currently running
            var activeAuctions = await _context.Auctions.CountAsync(a => a.Status == "Active");

            // Get User Account Of the Platform
            var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");

            // Get The Ballance of the Platform Account
            var platformWallet = platformUser != null
                ? await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id)
                : null;

            // Safety check Balance Either has Value or 0
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

            // create the profile and wallet
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
        public async Task<IActionResult> GetAllUsers([FromQuery] PaginationQueryDto query)
        {
            // Normalize the page number — never below 1
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size — default 20, hard cap at 100
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Free-text search term 
            var search = query.Search;

            // Optional role filter: Admin / Seller / Buyer
            var filter = query.Filter;

            // grab registered users, skip the platform system account
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

            // apply search filter — matches email, phone, full name, or city
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                // user ids whose profile full name or city matches
                var matchingProfileUserIds = await _context.UserProfiles
                    .Where(p => p.FullName.Contains(term) || (p.City != null && p.City.Contains(term)))
                    .Select(p => p.UserId)
                    .ToListAsync();

                userQuery = userQuery.Where(u =>
                    (u.Email != null && u.Email.Contains(term)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(term)) ||
                    matchingProfileUserIds.Contains(u.Id));
            }

            userQuery = userQuery.OrderBy(u => u.Email);

            var totalCount = await userQuery.CountAsync();

            var users = await userQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // load all profiles and wallets
            var profiles = await _context.UserProfiles.ToListAsync();
            var wallets = await _context.Wallets.ToListAsync();

            var userList = new List<object>();

            // build the response
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                // look up this user's profile / wallet
                var profile = profiles.FirstOrDefault(p => p.UserId == user.Id);
                var wallet = wallets.FirstOrDefault(w => w.UserId == user.Id);

                // build the profile part of the response
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

                // build the wallet part of the response 
                object? walletData = null;

                // If Wallaet found built it or else no need
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

            // Set the Lockout Value as Null
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


        // GET: api/Admin/auctions
        // Returns all auctions so admins can monitor them.
        [HttpGet("auctions")]
        public async Task<IActionResult> GetAllAuctionsForAdmin([FromQuery] PaginationQueryDto query)
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

            // Optional status filter: Upcoming / Active / Completed / Cancelled.
            var filter = query.Filter;

            var auctionsQuery = _context.Auctions.AsQueryable();

            // apply status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "NeedsSchedule")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == "Upcoming" && a.StartTime == null);
                }
                else if (filter == "Scheduled")
                {
                    auctionsQuery = auctionsQuery.Where(a =>
                        a.Status == "Upcoming" && a.StartTime != null);
                }
                else if (filter == "History")
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
            if (dispute.Status != "Open")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Only Open disputes can start review. Current status: {dispute.Status}."
                });
            }

            // Set Under Dispute What Ever 
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

            // already reviewed, don't credit the wallet twice
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
            depositRequest.Status = dto.Status;
            depositRequest.ReviewedAt = DateTime.UtcNow;
            depositRequest.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;

            // only approved deposits actually increase the balance
            if (dto.Status == "Approved")
            {
                // Increment Deposit Request Ammount + Previous Wallet Ballance
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
        public async Task<IActionResult> GetAllTransactions([FromQuery] PaginationQueryDto query)
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

            // Optional transaction-type filter: Credit / Debit / Penalty / Release / Commission
            var filter = query.Filter;

            var queryable = _context.WalletTransactions.AsQueryable();

            // apply type filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                queryable = queryable.Where(wt => wt.Type == filter);
            }

            // apply search filter — matches reference, user name, or user email
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

            // Global stats

            // Total volume = sum of Credit + Debit across ALL transactions.
            var totalVolume = await _context.WalletTransactions
                .Where(t => t.Type == "Credit" || t.Type == "Debit")
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            // Escrow currently held = sum of active SaleEscrow reservations.
            var totalEscrow = await _context.WalletReservations
                .Where(r => r.Type == "SaleEscrow" && r.Status == "Reserved")
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            // Active buyer eligibility deposits.
            var totalBuyerDeposits = await _context.WalletReservations
                .Where(r => r.Type == "BuyerFunds" && r.Status == "Reserved")
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            // Active seller deposits.
            var totalSellerDeposits = await _context.WalletReservations
                .Where(r => r.Type == "SellerDeposit" && r.Status == "Reserved")
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            // Count of active reservation
            var totalActiveReservations = await _context.WalletReservations
                .CountAsync(r => r.Status == "Reserved");

            // Paginated rows

            var transactions = await queryable
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(wt => new
                {
                    wt.Id,
                    wt.Type,
                    wt.Amount,
                    wt.BalanceAfter,
                    wt.Reference,
                    wt.CreatedAt,

                    // Get the user's full name using the wallet's UserId.
                    // If the name is not found, use "Unknown".
                    UserName = _context.UserProfiles
                        .Where(up => up.UserId == _context.Wallets
                            .Where(w => w.Id == wt.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(up => up.FullName)
                        .FirstOrDefault() ?? "Unknown",

                    // Get the user's email using the wallet's UserId.
                    // If the email is not found, use "Unknown".
                    UserEmail = _context.Users
                        .Where(u => u.Id == _context.Wallets
                            .Where(w => w.Id == wt.WalletId)
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
        // Returns all wallet reservations globally, for admin oversight.
        [HttpGet("reservations")]
        public async Task<IActionResult> GetAllReservations()
        {
            var reservations = await _context.WalletReservations
                .OrderByDescending(wr => wr.Id)
                .Select(wr => new
                {
                    wr.Id,
                    wr.Type,
                    wr.Amount,
                    wr.Status,
                    wr.ProductId,
                    wr.AuctionId,
                    wr.SaleId,

                    // grab the user's full name
                    UserName = _context.UserProfiles
                        .Where(up => up.UserId == _context.Wallets
                            .Where(w => w.Id == wr.WalletId)
                            .Select(w => w.UserId)
                            .FirstOrDefault())
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown",

                    // product title, only if this reservation is tied to a product
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
                data = reservations
            });
        }


        // GET: api/Admin/platform-wallet
        // Returns the platform system account's wallet and its transaction history.
        [HttpGet("platform-wallet")]
        public async Task<IActionResult> GetPlatformWallet([FromQuery] PaginationQueryDto query)
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

            // apply type filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                txQuery = txQuery.Where(t => t.Type == filter);
            }

            // apply search filter — matches reference or transaction type
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                txQuery = txQuery.Where(t =>
                    (t.Reference != null && t.Reference.Contains(term)) ||
                    t.Type.Contains(term));
            }

            txQuery = txQuery.OrderByDescending(t => t.CreatedAt);

            var totalCount = await txQuery.CountAsync();

            var transactions = await txQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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
        // Returns global counts of users per role (Admin / Seller / Buyer),
        // ignoring any search or filter — used by the stats cards on the Users page.
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

            // Count roles across the whole user base.
            var adminCount = 0;
            var sellerCount = 0;
            var buyerCount = 0;

            foreach (var userRole in userRoleRows)
            {
                // Find the role name for this role ID.
                var role = allRoles.FirstOrDefault(r => r.Id == userRole.RoleId);
                if (role == null) continue;

                if (role.Name == "Admin") adminCount++;
                else if (role.Name == "Seller") sellerCount++;
                else if (role.Name == "Buyer") buyerCount++;
            }

            return Ok(new
            {
                status = 200,
                message = "Role counts retrieved successfully.",
                data = new
                {
                    admin = adminCount,
                    seller = sellerCount,
                    buyer = buyerCount
                }
            });
        }
    }
}

