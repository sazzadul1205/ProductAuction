using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Dispute;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;
using ProductAuction.API.Services;
using System.Data;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DisputeController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IImageUploadService _imageUploadService;
        private readonly ILogger<DisputeController> _logger;

        public DisputeController(
            AppDbContext context,
            IImageUploadService imageUploadService,
            ILogger<DisputeController> logger)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _logger = logger;
        }


        // GET: api/dispute
        // Admin views all disputes.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllDisputes(
    [FromQuery] ProductAuction.API.DTOs.Common.PaginationQueryDto query)
        {
            // Pagination settings.
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

            // Get all disputes.
            var disputesQuery = _context.Disputes.AsNoTracking();

            // Filter by status.
            var status = !string.IsNullOrWhiteSpace(query.Filter)
                ? query.Filter
                : query.Status;

            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<DisputeStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                disputesQuery = disputesQuery.Where(d => d.Status == parsedStatus);
            }

            // Search disputes.
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim().ToLower();

                disputesQuery = disputesQuery.Where(d =>
                    (d.Complaint != null && d.Complaint.ToLower().Contains(search)) ||
                    d.Id.ToString().Contains(search) ||
                    d.SaleId.ToString().Contains(search) ||
                    d.ProductId.ToString().Contains(search) ||
                    (d.EvidenceUrl != null && d.EvidenceUrl.ToLower().Contains(search))
                );
            }

            // Get total.
            var totalCount = await disputesQuery.CountAsync();

            // Get counts for all dispute statuses.
            var openCount = await _context.Disputes.CountAsync(d => d.Status == DisputeStatus.Open);
            var underReviewCount = await _context.Disputes.CountAsync(d => d.Status == DisputeStatus.UnderReview);
            var resolvedCount = await _context.Disputes.CountAsync(d => d.Status == DisputeStatus.Resolved || d.Status == DisputeStatus.Rejected);

            // Get paginated disputes.
            var disputes = await disputesQuery
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get related sale information.
            var saleIds = disputes.Select(d => d.SaleId).Distinct().ToList();

            var sales = await _context.Sales
                .Where(s => saleIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);

            // Find users involved in the disputes.
            var raisedByIds = disputes.Select(d => d.RaisedBy).Distinct().ToList();
            var againstUserIds = new List<string>();

            foreach (var dispute in disputes)
            {
                if (sales.TryGetValue(dispute.SaleId, out var sale))
                {
                    var againstUserId = sale.BuyerId == dispute.RaisedBy
                        ? sale.SellerId
                        : sale.BuyerId;

                    if (!string.IsNullOrEmpty(againstUserId))
                    {
                        againstUserIds.Add(againstUserId);
                    }
                }
            }

            var allUserIds = raisedByIds
                .Concat(againstUserIds)
                .Distinct()
                .ToList();

            // Get user profiles.
            var profiles = await _context.UserProfiles
                .Where(p => allUserIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, p => p.FullName);

            // Get user roles.
            var roles = await _context.UserRoles
                .Where(ur => allUserIds.Contains(ur.UserId))
                .Join(
                    _context.Roles,
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => new { ur.UserId, RoleName = r.Name }
                )
                .GroupBy(x => x.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    g.First().RoleName
                })
                .ToDictionaryAsync(x => x.UserId, x => x.RoleName);

            // Get penalties for these disputes.
            var disputeIds = disputes.Select(d => d.Id).ToList();

            var penalties = await _context.Penalties
                .Where(p => disputeIds.Contains(p.DisputeId))
                .ToDictionaryAsync(p => p.DisputeId, p => new
                {
                    p.TotalAmount,
                    p.OppositePartyAmount,
                    p.PlatformAmount,
                    p.AgainstUserId
                });

            // Prepare final response.
            var result = disputes.Select(d =>
            {
                sales.TryGetValue(d.SaleId, out var sale);
                penalties.TryGetValue(d.Id, out var penalty);

                var againstUserId = sale == null
                    ? null
                    : (sale.BuyerId == d.RaisedBy ? sale.SellerId : sale.BuyerId);

                return new
                {
                    d.Id,
                    d.SaleId,
                    d.ProductId,
                    d.RaisedBy,
                    d.Complaint,
                    d.EvidenceUrl,
                    Status = d.Status.ToString(),
                    d.AdminDecision,
                    d.DecidedBy,
                    d.CreatedAt,

                    RaisedByName = profiles.TryGetValue(d.RaisedBy, out var raisedName)
                        ? raisedName
                        : "Unknown",

                    RaisedByRole = roles.TryGetValue(d.RaisedBy, out var raisedRole)
                        ? raisedRole
                        : "User",

                    AgainstUserId = againstUserId,

                    AgainstName = !string.IsNullOrEmpty(againstUserId) &&
                                  profiles.TryGetValue(againstUserId, out var againstName)
                        ? againstName
                        : "Unknown",

                    AgainstRole = !string.IsNullOrEmpty(againstUserId) &&
                                  roles.TryGetValue(againstUserId, out var againstRole)
                        ? againstRole
                        : "User",

                    Penalty = penalty
                };
            }).ToList();

            return Ok(new
            {
                status = 200,
                message = "Disputes retrieved successfully.",
                data = result,
                page,
                pageSize,
                totalCount,
                openCount,
                underReviewCount,
                resolvedCount
            });
        }


        // POST: api/dispute
        // Buyer or seller submits a dispute for a sale.
        [HttpPost]
        public async Task<IActionResult> CreateDispute([FromForm] CreateDisputeDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User information was not found."
                });
            }
            if (dto == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Dispute data is required."
                });
            }

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == dto.SaleId);
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // only the buyer or seller involved can dispute it
            if (sale.BuyerId != userId && sale.SellerId != userId)
            {
                return Forbid();
            }
            if (sale.Status == SaleStatus.AwaitingPayment)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute cannot be submitted before payment."
                });
            }
            if (sale.Status == SaleStatus.Completed)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute cannot be raised for a completed sale."
                });
            }

            bool isSeller = sale.SellerId == userId;
            if (isSeller && DateTime.UtcNow <= sale.PaymentDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The seller can only raise a dispute after the payment window has ended."
                });
            }

            var existingDispute = await _context.Disputes.AnyAsync(d => d.SaleId == sale.Id);
            if (existingDispute)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute already exists for this sale."
                });
            }

            string? evidenceUrl = null;
            if (dto.EvidenceImage != null)
            {
                evidenceUrl = await _imageUploadService.UploadImageAsync(dto.EvidenceImage);
            }

            // create the dispute
            var dispute = new Dispute
            {
                SaleId = sale.Id,
                ProductId = sale.ProductId,
                RaisedBy = userId,
                Complaint = dto.Complaint,
                EvidenceUrl = evidenceUrl,
                Status = DisputeStatus.Open,
                CreatedAt = DateTime.UtcNow
            };

            sale.Status = SaleStatus.Disputed;

            _context.Disputes.Add(dispute);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Dispute submitted successfully. Awaiting admin review.",
                data = new
                {
                    dispute.Id,
                    dispute.SaleId,
                    dispute.ProductId,
                    dispute.RaisedBy,
                    dispute.Complaint,
                    dispute.EvidenceUrl,
                    Status = dispute.Status.ToString(),
                    dispute.CreatedAt
                }
            });
        }


        // PUT: api/dispute/{id}/resolve
        // Admin decides who's at fault and resolves the dispute.
        [HttpPut("{id}/resolve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ResolveDispute(int id, ResolveDisputeDto dto)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Admin information was not found."
                });
            }
            if (dto.FaultType != "Buyer" && dto.FaultType != "Seller" && dto.FaultType != "System")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "FaultType must be Buyer, Seller, or System."
                });
            }
            if (string.IsNullOrEmpty(dto.AdminDecision))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "An admin decision is required."
                });
            }
            if (dto.FaultType == "System" && dto.ApplyPenalty)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A system fault cannot receive a penalty."
                });
            }

            // Start Transaction
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var dispute = await _context.Disputes.FirstOrDefaultAsync(d => d.Id == id);
            if (dispute == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Dispute not found."
                });
            }

            // dispute has to be under revie
            if (dispute.Status != DisputeStatus.UnderReview)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Disputes must be 'Under Review' before resolving. Current status: {dispute.Status}."
                });
            }

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == dispute.SaleId);
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }


            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == sale.ProductId);
            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related product not found."
                });
            }
            if (await _context.Penalties.AnyAsync(p => p.DisputeId == dispute.Id))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A penalty record already exists for this dispute."
                });
            }

            // find any active sale escrow
            var escrow = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.SaleId == sale.Id &&
                    r.Type == ReservationType.SaleEscrow &&
                    r.Status == ReservationStatus.Reserved);


            if (sale.PaymentSubmittedAt.HasValue && escrow == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Payment was submitted, but no active escrow was found"
                });
            }
            if (escrow != null && escrow.Amount != sale.FinalAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The escrow amount does not match the sale amount."
                });
            }

            // buyer-fault penalty with money in escrow needs manual handling
            if (dto.FaultType == "Buyer" && dto.ApplyPenalty && escrow != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Sale payment remains in escrow."
                });
            }

            // save the admin's decision
            dispute.AdminDecision = dto.AdminDecision.Trim();
            dispute.DecidedBy = adminId;

            // deposit is 25% of the starting price, split 80/20 between
            decimal productValue = product.StartingPrice;
            decimal totalPenalty = Math.Round(product.StartingPrice * 0.25m, 2);
            decimal oppositePartyAmount = Math.Round(totalPenalty * 0.80m, 2);
            decimal platformAmount = totalPenalty - oppositePartyAmount;

            // OPTION 1: system fault
            // no penalty, just refund escrow and release deposits
            if (dto.FaultType == "System")
            {
                if (escrow != null)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);
                    if (buyerWallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The buyer's wallet was not found."
                        });
                    }

                    buyerWallet.Balance += escrow.Amount;
                    escrow.Status = ReservationStatus.Released;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = WalletTransactionType.Credit,
                        Amount = escrow.Amount,
                        BalanceAfter = buyerWallet.Balance,
                        Reference = $"System-fault refund for dispute #{dispute.Id}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // OPTION 2: buyer fault, no penalty
            // don't forfeit the buyer's deposit, just refund escrow
            else if (dto.FaultType == "Buyer" && !dto.ApplyPenalty)
            {
                if (escrow != null)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);
                    if (buyerWallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The buyer's wallet was not found."
                        });
                    }

                    buyerWallet.Balance += escrow.Amount;
                    escrow.Status = ReservationStatus.Released;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = WalletTransactionType.Credit,
                        Amount = escrow.Amount,
                        BalanceAfter = buyerWallet.Balance,
                        Reference = $"No-penalty refund for dispute #{dispute.Id}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // OPTION 3: seller fault, no penalty
            // don't forfeit the seller's deposit, just refund escrow
            else if (dto.FaultType == "Seller" && !dto.ApplyPenalty)
            {
                if (escrow != null)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);
                    if (buyerWallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The buyer's wallet was not found."
                        });
                    }

                    buyerWallet.Balance += escrow.Amount;
                    escrow.Status = ReservationStatus.Released;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = WalletTransactionType.Credit,
                        Amount = escrow.Amount,
                        BalanceAfter = buyerWallet.Balance,
                        Reference = $"Seller-fault refund for dispute #{dispute.Id}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // OPTION 4: buyer fault, penalty applied
            // forfeit the buyer's deposit and split it 80/20
            else if (dto.FaultType == "Buyer" && dto.ApplyPenalty)
            {
                var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);
                if (buyerWallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The buyer's wallet was not found."
                    });
                }

                // find the buyer's eligibility deposit for this auction
                var buyerDeposit = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.WalletId == buyerWallet.Id &&
                        r.AuctionId == sale.AuctionId &&
                        r.Type == ReservationType.BuyerFunds &&
                        r.Status == ReservationStatus.Reserved);

                if (buyerDeposit == null || buyerDeposit.Amount < totalPenalty || buyerWallet.ReservedBalance < buyerDeposit.Amount || buyerWallet.Balance < totalPenalty)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The buyer's reserved deposit is missing or does not match the penalty amount."
                    });
                }

                // seller gets the 80%
                var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);
                if (sellerWallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The seller's wallet was not found."
                    });
                }

                // platform gets the 20%
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

                // forfeit the buyer's deposit
                buyerWallet.Balance -= totalPenalty;
                buyerWallet.ReservedBalance -= buyerDeposit.Amount;
                buyerDeposit.Status = ReservationStatus.Forfeited;

                // split it
                sellerWallet.Balance += oppositePartyAmount;
                platformWallet.Balance += platformAmount;

                var now = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = buyerWallet.Id,
                    Type = WalletTransactionType.Penalty,
                    Amount = totalPenalty,
                    BalanceAfter = buyerWallet.Balance,
                    Reference = $"Buyer penalty for dispute #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = sellerWallet.Id,
                    Type = WalletTransactionType.Credit,
                    Amount = oppositePartyAmount,
                    BalanceAfter = sellerWallet.Balance,
                    Reference = $"Dispute compensation #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = platformWallet.Id,
                    Type = WalletTransactionType.Penalty,
                    Amount = platformAmount,
                    BalanceAfter = platformWallet.Balance,
                    Reference = $"Platform penalty share for dispute #{dispute.Id}",
                    CreatedAt = now
                });

                _context.Penalties.Add(new Penalty
                {
                    DisputeId = dispute.Id,
                    AgainstUserId = sale.BuyerId,
                    ProductValue = productValue,
                    TotalAmount = totalPenalty,
                    OppositePartyAmount = oppositePartyAmount,
                    PlatformAmount = platformAmount,
                    Status = PenaltyStatus.Applied
                });
            }

            // OPTION 5: seller fault, penalty applied
            // refund escrow, forfeit seller's deposit, split it 80/20
            else if (dto.FaultType == "Seller" && dto.ApplyPenalty)
            {
                // refund the buyer's sale payment first
                if (escrow != null)
                {
                    var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);
                    if (buyerWallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The buyer's wallet was not found."
                        });
                    }

                    buyerWallet.Balance += escrow.Amount;
                    escrow.Status = ReservationStatus.Released;

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = WalletTransactionType.Credit,
                        Amount = escrow.Amount,
                        BalanceAfter = buyerWallet.Balance,
                        Reference = $"Seller-fault refund for dispute #{dispute.Id}",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // find the seller's wallet and deposit
                var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);
                if (sellerWallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The seller's wallet was not found."
                    });
                }

                var sellerDeposit = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.WalletId == sellerWallet.Id &&
                        r.ProductId == sale.ProductId &&
                        r.Type == ReservationType.SellerDeposit &&
                        r.Status == ReservationStatus.Reserved);

                if (sellerDeposit == null || sellerDeposit.Amount < totalPenalty || sellerWallet.ReservedBalance < sellerDeposit.Amount || sellerWallet.Balance < totalPenalty)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The seller's reserved deposit is missing or does not match the penalty amount."
                    });
                }

                // buyer gets the 80%
                var buyerWalletForPenalty = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);

                if (buyerWalletForPenalty == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The buyer's wallet was not found."
                    });
                }

                // platform gets the 20%
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

                // forfeit the seller's deposit
                sellerWallet.Balance -= totalPenalty;
                sellerWallet.ReservedBalance -= sellerDeposit.Amount;
                sellerDeposit.Status = ReservationStatus.Forfeited;

                // split it
                buyerWalletForPenalty.Balance += oppositePartyAmount;
                platformWallet.Balance += platformAmount;

                var now = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = sellerWallet.Id,
                    Type = WalletTransactionType.Penalty,
                    Amount = totalPenalty,
                    BalanceAfter = sellerWallet.Balance,
                    Reference = $"Seller penalty for dispute #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = buyerWalletForPenalty.Id,
                    Type = WalletTransactionType.Credit,
                    Amount = oppositePartyAmount,
                    BalanceAfter = buyerWalletForPenalty.Balance,
                    Reference = $"Dispute compensation #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = platformWallet.Id,
                    Type = WalletTransactionType.Penalty,
                    Amount = platformAmount,
                    BalanceAfter = platformWallet.Balance,
                    Reference = $"Platform penalty share for dispute #{dispute.Id}",
                    CreatedAt = now
                });

                _context.Penalties.Add(new Penalty
                {
                    DisputeId = dispute.Id,
                    AgainstUserId = sale.SellerId,
                    ProductValue = productValue,
                    TotalAmount = totalPenalty,
                    OppositePartyAmount = oppositePartyAmount,
                    PlatformAmount = platformAmount,
                    Status = PenaltyStatus.Applied
                });
            }

            // release the seller's deposit if it wasn't forfeited
            var sellerWalletForRelease = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);

            if (sellerWalletForRelease != null)
            {
                var sellerDepositToRelease = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.WalletId == sellerWalletForRelease.Id &&
                        r.ProductId == sale.ProductId &&
                        r.Type == ReservationType.SellerDeposit &&
                        r.Status == ReservationStatus.Reserved);

                if (sellerDepositToRelease != null)
                {
                    sellerWalletForRelease.ReservedBalance -= sellerDepositToRelease.Amount;
                    sellerDepositToRelease.Status = ReservationStatus.Released;
                }
            }

            // release the buyer's deposit if it wasn't forfeited
            var buyerWalletForRelease = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);

            if (buyerWalletForRelease != null)
            {
                var buyerDepositToRelease = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.WalletId == buyerWalletForRelease.Id &&
                        r.AuctionId == sale.AuctionId &&
                        r.Type == ReservationType.BuyerFunds &&
                        r.Status == ReservationStatus.Reserved);

                if (buyerDepositToRelease != null)
                {
                    buyerWalletForRelease.ReservedBalance -= buyerDepositToRelease.Amount;
                    buyerDepositToRelease.Status = ReservationStatus.Released;
                }
            }

            // release every other buyer's eligibility deposit for this auction.
            var currentBuyerWalletId = buyerWalletForRelease?.Id ?? 0;

            var otherBuyerReservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == sale.AuctionId &&
                    r.Type == ReservationType.BuyerFunds &&
                    r.Status == ReservationStatus.Reserved &&
                    r.WalletId != currentBuyerWalletId)
                .ToListAsync();

            foreach (var reservation in otherBuyerReservations)
            {
                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

                if (wallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = $"Wallet for reservation #{reservation.Id} was not found."
                    });
                }

                wallet.ReservedBalance -= reservation.Amount;
                reservation.Status = ReservationStatus.Released;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = WalletTransactionType.Release,
                    Amount = reservation.Amount,
                    BalanceAfter = wallet.Balance,
                    Reference = $"Auction deposit released after dispute #{dispute.Id}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // wrap it up
            dispute.Status = DisputeStatus.Resolved;
            product.Status = ProductStatus.Unavailable;
            sale.Status = SaleStatus.Refunded;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = dto.ApplyPenalty
                    ? "Dispute resolved and penalty applied successfully."
                    : "Dispute resolved without a penalty.",
                data = new
                {
                    dispute.Id,
                    Status = dispute.Status.ToString(),
                    dto.FaultType,
                    dispute.AdminDecision,
                    dispute.DecidedBy,
                    ProductStatus = product.Status.ToString(),
                    SaleStatus = sale.Status.ToString(),
                    PenaltyApplied = dto.ApplyPenalty,
                    Penalty = dto.ApplyPenalty
                        ? new
                        {
                            ProductValue = productValue,
                            TotalAmount = totalPenalty,
                            OppositePartyAmount = oppositePartyAmount,
                            PlatformAmount = platformAmount,
                            Status = "Applied"
                        }
                        : null
                }
            });
        }
    }
}