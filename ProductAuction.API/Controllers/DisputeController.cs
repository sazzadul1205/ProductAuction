using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Dispute;
using ProductAuction.API.Models;
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

        public DisputeController(AppDbContext context, IImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }


        // GET: api/dispute
        // Admin views all disputes.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllDisputes()
        {
            // fetch base dispute data
            var disputes = await _context.Disputes
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            // fetch all the sales these disputes point to
            var saleIds = disputes.Select(d => d.SaleId).Distinct().ToList();

            var sales = await _context.Sales
                .Where(s => saleIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);

            // collect every user id involved (both sides of each sale)
            var raisedByIds = disputes.Select(d => d.RaisedBy).Distinct().ToList();
            var againstUserIds = new List<string>();

            foreach (var d in disputes)
            {
                if (sales.TryGetValue(d.SaleId, out var sale))
                {
                    var againstId = sale.BuyerId == d.RaisedBy ? sale.SellerId : sale.BuyerId;

                    if (!string.IsNullOrEmpty(againstId))
                        againstUserIds.Add(againstId);
                }
            }

            var allUserIds = raisedByIds.Concat(againstUserIds).Distinct().ToList();

            // fetch profiles and roles for those users
            var profiles = await _context.UserProfiles
                .Where(p => allUserIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, p => p.FullName);

            var roles = await _context.UserRoles
                .Where(ur => allUserIds.Contains(ur.UserId))
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleName = r.Name })
                .GroupBy(x => x.UserId)
                .Select(g => new { UserId = g.Key, RoleName = g.First().RoleName })
                .ToDictionaryAsync(x => x.UserId, x => x.RoleName);

            // fetch penalties for these disputes
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

            // combine everything in memory
            var result = disputes.Select(d =>
            {
                sales.TryGetValue(d.SaleId, out var sale);

                var againstUserId = sale != null
                    ? (sale.BuyerId == d.RaisedBy ? sale.SellerId : sale.BuyerId)
                    : null;

                penalties.TryGetValue(d.Id, out var penalty);

                return new
                {
                    d.Id,
                    d.SaleId,
                    d.ProductId,
                    d.RaisedBy,
                    d.Complaint,
                    d.EvidenceUrl,
                    d.Status,
                    d.AdminDecision,
                    d.DecidedBy,
                    d.CreatedAt,

                    RaisedByName = profiles.TryGetValue(d.RaisedBy, out var rName) ? rName : "Unknown",
                    RaisedByRole = roles.TryGetValue(d.RaisedBy, out var rRole) ? rRole : "User",

                    AgainstUserId = againstUserId,

                    AgainstName = !string.IsNullOrEmpty(againstUserId) && profiles.TryGetValue(againstUserId, out var aName)
                        ? aName
                        : "Unknown",

                    AgainstRole = !string.IsNullOrEmpty(againstUserId) && roles.TryGetValue(againstUserId, out var aRole)
                        ? aRole
                        : "User",

                    Penalty = penalty
                };
            }).ToList();

            return Ok(new
            {
                status = 200,
                message = "Disputes retrieved successfully.",
                data = result
            });
        }


        // POST: api/dispute
        // Buyer or seller submits a dispute for a sale.
        [HttpPost]
        public async Task<IActionResult> CreateDispute([FromForm] CreateDisputeDto dto)
        {
            // get the logged-in user's id
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User information was not found."
                });
            }

            // find the sale this dispute is about
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
                return Forbid();

            // can't dispute before payment — nothing has gone wrong yet
            if (sale.Status == "AwaitingPayment")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute cannot be submitted before payment."
                });
            }

            // can't dispute something that's already done
            if (sale.Status == "Completed")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute cannot be raised for a completed sale."
                });
            }

            // sellers have to wait until the payment window closes before accusing the buyer
            bool isSeller = sale.SellerId == userId;

            if (isSeller && DateTime.UtcNow <= sale.PaymentDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The seller can only raise a dispute after the payment window has ended."
                });
            }

            // one dispute per sale, no duplicates
            var existingDispute = await _context.Disputes.AnyAsync(d => d.SaleId == sale.Id);

            if (existingDispute)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute already exists for this sale."
                });
            }

            // evidence is image-only — upload it if the user attached one
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
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            // mark the sale as disputed so it can't complete while under review
            sale.Status = "Disputed";

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
                    dispute.Status,
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

            // fault type has to be one of the three expected values
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

            // system fault can't get a penalty — nobody's fault
            if (dto.FaultType == "System" && dto.ApplyPenalty)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A system fault cannot receive a penalty."
                });
            }

            // all the wallet + status changes below need to happen together
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

            // dispute has to be under review first
            if (dispute.Status != "UnderReview")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Disputes must be 'Under Review' before resolving. Current status: {dispute.Status}."
                });
            }

            // load the related sale
            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == dispute.SaleId);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            // load the product that was sold
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == sale.ProductId);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related product not found."
                });
            }

            // don't create two penalty records for the same dispute
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
                    r.Type == "SaleEscrow" &&
                    r.Status == "Reserved");

            // if payment was submitted, escrow must exist 
            if (sale.PaymentSubmittedAt.HasValue && escrow == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Payment was submitted, but no active escrow was found. Admin investigation is required."
                });
            }

            //  escrow amount should match the sale amount
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
                    message = "Sale payment remains in escrow. Admin review is required before applying a buyer-fault penalty."
                });
            }

            // save the admin's decision
            dispute.AdminDecision = dto.AdminDecision.Trim();
            dispute.DecidedBy = adminId;

            // deposit is 25% of the starting price, split 80/20 between
            // the opposite party and the platform
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
                    escrow.Status = "Released";

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = "Credit",
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
                    var buyerWallet = await _context.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);

                    if (buyerWallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The buyer's wallet was not found."
                        });
                    }

                    buyerWallet.Balance += escrow.Amount;
                    escrow.Status = "Released";

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = "Credit",
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
                    escrow.Status = "Released";

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = "Credit",
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
                        r.Type == "BuyerFunds" &&
                        r.Status == "Reserved");

                if (buyerDeposit == null ||
                    buyerDeposit.Amount != totalPenalty ||
                    buyerWallet.ReservedBalance < buyerDeposit.Amount ||
                    buyerWallet.Balance < totalPenalty)
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
                buyerDeposit.Status = "Forfeited";

                // split it
                sellerWallet.Balance += oppositePartyAmount;
                platformWallet.Balance += platformAmount;

                var now = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = buyerWallet.Id,
                    Type = "Penalty",
                    Amount = totalPenalty,
                    BalanceAfter = buyerWallet.Balance,
                    Reference = $"Buyer penalty for dispute #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = sellerWallet.Id,
                    Type = "Credit",
                    Amount = oppositePartyAmount,
                    BalanceAfter = sellerWallet.Balance,
                    Reference = $"Dispute compensation #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = platformWallet.Id,
                    Type = "Penalty",
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
                    Status = "Applied"
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
                    escrow.Status = "Released";

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = "Credit",
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
                        r.Type == "SellerDeposit" &&
                        r.Status == "Reserved");

                if (sellerDeposit == null ||
                    sellerDeposit.Amount != totalPenalty ||
                    sellerWallet.ReservedBalance < sellerDeposit.Amount ||
                    sellerWallet.Balance < totalPenalty)
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
                sellerDeposit.Status = "Forfeited";

                // split it
                buyerWalletForPenalty.Balance += oppositePartyAmount;
                platformWallet.Balance += platformAmount;

                var now = DateTime.UtcNow;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = sellerWallet.Id,
                    Type = "Penalty",
                    Amount = totalPenalty,
                    BalanceAfter = sellerWallet.Balance,
                    Reference = $"Seller penalty for dispute #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = buyerWalletForPenalty.Id,
                    Type = "Credit",
                    Amount = oppositePartyAmount,
                    BalanceAfter = buyerWalletForPenalty.Balance,
                    Reference = $"Dispute compensation #{dispute.Id}",
                    CreatedAt = now
                });

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = platformWallet.Id,
                    Type = "Penalty",
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
                    Status = "Applied"
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
                        r.Type == "SellerDeposit" &&
                        r.Status == "Reserved");

                if (sellerDepositToRelease != null)
                {
                    sellerWalletForRelease.ReservedBalance -= sellerDepositToRelease.Amount;
                    sellerDepositToRelease.Status = "Released";
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
                        r.Type == "BuyerFunds" &&
                        r.Status == "Reserved");

                if (buyerDepositToRelease != null)
                {
                    buyerWalletForRelease.ReservedBalance -= buyerDepositToRelease.Amount;
                    buyerDepositToRelease.Status = "Released";
                }
            }

            // release every other buyer's eligibility deposit for this auction.
            // skip the current buyer's wallet — otherwise we'd deduct twice,
            // since we just changed its status in memory but haven't saved yet.
            var currentBuyerWalletId = buyerWalletForRelease?.Id ?? 0;

            var otherBuyerReservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == sale.AuctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved" &&
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
                reservation.Status = "Released";

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = "Release",
                    Amount = reservation.Amount,
                    BalanceAfter = wallet.Balance,
                    Reference = $"Auction deposit released after dispute #{dispute.Id}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // wrap it up
            dispute.Status = "Resolved";
            product.Status = "Unavailable";
            sale.Status = "Refunded";

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
                    dispute.Status,
                    dto.FaultType,
                    dispute.AdminDecision,
                    dispute.DecidedBy,
                    ProductStatus = product.Status,
                    SaleStatus = sale.Status,
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