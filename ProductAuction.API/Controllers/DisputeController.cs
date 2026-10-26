
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Dispute;
using ProductAuction.API.Models;
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

        public DisputeController(AppDbContext context)
        {
            _context = context;
        }


        // GET: api/dispute
        // Admin views all disputes.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllDisputes()
        {
            var disputes = await _context.Disputes.OrderByDescending(d => d.CreatedAt).ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Disputes retrieved successfully.",
                data = disputes
            });
        }


        // GET: api/dispute/my-disputes
        // Returns all disputes related to the currently logged-in user.
        [HttpGet("my-disputes")]
        public async Task<IActionResult> GetMyDisputes()
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

            // Find disputes where the logged-in user is either
            // the buyer or the seller of the related sale.
            var disputes = await _context.Disputes
                .Where(d => _context.Sales.Any(s =>
                    // Match the sale with the dispute using SaleId.
                    s.Id == d.SaleId &&

                    // Only include sales involving the logged-in user.
                    (s.BuyerId == userId || s.SellerId == userId)))
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your disputes retrieved successfully.",
                data = disputes
            });
        }


        // GET: api/dispute/1
        // Admin, buyer, or seller views a specific dispute.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDisputeById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var dispute = await _context.Disputes
                .FirstOrDefaultAsync(d => d.Id == id);

            if (dispute == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Dispute not found."
                });
            }

            // Find the related sale to check who is involved.
            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == dispute.SaleId);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            // Only the buyer, seller, or admin can view this dispute.
            if (!isAdmin &&
                sale.BuyerId != userId &&
                sale.SellerId != userId)
            {
                return Forbid();
            }

            return Ok(new
            {
                status = 200,
                message = "Dispute retrieved successfully.",
                data = dispute
            });
        }

        // POST: api/dispute
        // Buyer or seller submits a dispute for a sale.
        [HttpPost]
        public async Task<IActionResult> CreateDispute(CreateDisputeDto dto)
        {
            // Get the ID of the logged-in user.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User information was not found."
                });
            }

            // Find the sale related to this dispute.
            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == dto.SaleId);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // Only the buyer or seller involved in this sale can dispute it.
            if (sale.BuyerId != userId && sale.SellerId != userId)
            {
                return Forbid();
            }

            // Do not allow disputes for sales that have not reached
            // a stage where a transaction or delivery issue can occur.
            if (sale.Status == "AwaitingPayment")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute cannot be submitted before payment."
                });
            }

            // A completed sale cannot be disputed again.
            if (sale.Status == "Completed")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute cannot be raised for a completed sale."
                });
            }

            // Sellers may only raise a dispute after the 3-day payment
            // window has closed. This gives the buyer time to pay before
            // the seller can accuse them of non-payment.
            bool isSeller = sale.SellerId == userId;
            if (isSeller && DateTime.UtcNow <= sale.PaymentDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The seller can only raise a dispute after the payment window has ended."
                });
            }

            // Prevent duplicate disputes for the same sale.
            var existingDispute = await _context.Disputes.AnyAsync(d => d.SaleId == sale.Id);

            if (existingDispute)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A dispute already exists for this sale."
                });
            }

            // Create the dispute record.
            var dispute = new Dispute
            {
                SaleId = sale.Id,
                ProductId = sale.ProductId,
                RaisedBy = userId,
                Complaint = dto.Complaint,
                EvidenceUrl = string.IsNullOrWhiteSpace(dto.EvidenceUrl)
                    ? null
                    : dto.EvidenceUrl,
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            // Mark the sale as disputed so normal completion
            // cannot proceed while the admin reviews the complaint.
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
        // Admin decides the responsible party and resolves the dispute.
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

            // Validate the admin's decision.
            if (dto.FaultType != "Buyer" &&
                dto.FaultType != "Seller" &&
                dto.FaultType != "System")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "FaultType must be Buyer, Seller, or System."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.AdminDecision))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "An admin decision is required."
                });
            }

            // System fault cannot result in a buyer or seller penalty.
            if (dto.FaultType == "System" && dto.ApplyPenalty)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A system fault cannot receive a penalty."
                });
            }



            // Keep all database changes in one transaction.
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

            // A finalized dispute must not be processed again.
            if (dispute.Status != "Open" &&
                dispute.Status != "UnderReview")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This dispute has already been finalized."
                });
            }

            // Load the sale this dispute belongs to.
            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == dispute.SaleId);

            // A dispute pointing to a missing sale means inconsistent data
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            // Load the product that was sold in this sale.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == sale.ProductId);

            // A sale pointing to a missing product means inconsistent data.
            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related product not found."
                });
            }

            // Prevent duplicate penalty records.
            if (await _context.Penalties.AnyAsync(p => p.DisputeId == dispute.Id))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A penalty record already exists for this dispute."
                });
            }

            // Find any active sale escrow.
            var escrow = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.SaleId == sale.Id &&
                    r.Type == "SaleEscrow" &&
                    r.Status == "Reserved");

            // A submitted payment must have its corresponding active escrow.
            if (sale.PaymentSubmittedAt.HasValue && escrow == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Payment was submitted, but no active escrow was found. Admin investigation is required."
                });
            }

            // Sanity check on the escrow, only if one exists.
            if (escrow != null && escrow.Amount != sale.FinalAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The escrow amount does not match the sale amount."
                });
            }

            // Buyer-fault penalty with sale money in escrow needs manual review.
            if (dto.FaultType == "Buyer" &&
                dto.ApplyPenalty &&
                escrow != null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Sale payment remains in escrow. Admin review is required before applying a buyer-fault penalty."
                });
            }

            // Save the admin's decision.
            dispute.AdminDecision = dto.AdminDecision.Trim();
            dispute.DecidedBy = adminId;

            // The deposit is 25% of the product's starting price.
            // Distribute the entire deposit: 80% to the opposite party,
            // and 20% to the platform.
            decimal productValue = product.StartingPrice;
            decimal totalPenalty = Math.Round(product.StartingPrice * 0.25m, 2);
            decimal oppositePartyAmount = Math.Round(totalPenalty * 0.80m, 2);
            decimal platformAmount =totalPenalty - oppositePartyAmount;

            // OPTION 1: SYSTEM FAULT
            // No penalty. Refund escrow and release the deposits.
            if (dto.FaultType == "System")
            {
                // Refund sale escrow, if one exists.
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


            // OPTION 2: BUYER FAULT, NO PENALTY
            // Do not forfeit the buyer's deposit. Refund escrow if present.
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


            // OPTION 3: SELLER FAULT, NO PENALTY
            // Do not forfeit the seller's deposit. Refund escrow if present.
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

            // OPTION 4: BUYER FAULT, PENALTY APPLIED
            // Forfeit the buyer's deposit and distribute the penalty.
            else if (dto.FaultType == "Buyer" && dto.ApplyPenalty)
            {
                // Find the buyer's wallet.
                var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);

                if (buyerWallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The buyer's wallet was not found."
                    });
                }

                // Find the buyer's one eligibility deposit for this auction.
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

                // Find the seller's wallet to receive the 20% compensation.
                var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);

                if (sellerWallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The seller's wallet was not found."
                    });
                }

                // Find the platform wallet for the remaining 5%.
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

                // Forfeit the buyer's deposit. Do not deduct the penalty twice.
                buyerWallet.Balance -= totalPenalty;
                buyerWallet.ReservedBalance -= buyerDeposit.Amount;
                buyerDeposit.Status = "Forfeited";

                // Distribute the forfeited amount.
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

            // OPTION 5: SELLER FAULT, PENALTY APPLIED
            // Forfeit the seller's deposit, refund escrow if present,
            // and distribute the penalty.
            else if (dto.FaultType == "Seller" && dto.ApplyPenalty)
            {
                // Refund the buyer's sale payment before applying the penalty.
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

                // Find the seller's wallet and deposit.
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

                // Find the buyer's wallet to receive the 20% compensation.
                var buyerWalletForPenalty = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.BuyerId);

                if (buyerWalletForPenalty == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The buyer's wallet was not found."
                    });
                }

                // Find the platform wallet for the remaining 5%.
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

                // Forfeit the seller's deposit. Do not deduct the penalty twice.
                sellerWallet.Balance -= totalPenalty;
                sellerWallet.ReservedBalance -= sellerDeposit.Amount;
                sellerDeposit.Status = "Forfeited";

                // Distribute the forfeited amount.
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

            // Release the seller's deposit if it was not forfeited.
            // A forfeited deposit is no longer Reserved, so it is skipped.
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

            // Release the buyer's deposit if it was not forfeited.
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

            // Release all other buyers' eligibility deposits for this auction.
            // Deposits already marked Forfeited are not selected here.
            var otherBuyerReservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == sale.AuctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
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

            // Finalize the dispute without deleting historical records.
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
