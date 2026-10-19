
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
        // Admin reviews a dispute and decides who is responsible.
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

            // A system fault must not penalize the buyer or seller.
            if (dto.FaultType == "System" && dto.ApplyPenalty)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A system fault cannot receive a buyer or seller penalty."
                });
            }

            await using var transaction = await _context.Database
                .BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

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

            if (dispute.Status != "Open" &&
                dispute.Status != "UnderReview")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This dispute has already been finalized."
                });
            }

            var sale = await _context.Sales
                .FirstOrDefaultAsync(s => s.Id == dispute.SaleId);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == sale.ProductId);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related product not found."
                });
            }

            // Identify the responsible party from the admin's decision.
            string? responsibleUserId = null;

            if (dto.FaultType == "Buyer")
            {
                responsibleUserId = sale.BuyerId;
            }
            else if (dto.FaultType == "Seller")
            {
                responsibleUserId = sale.SellerId;
            }

            // Prevent duplicate penalty records.
            var existingPenalty = await _context.Penalties
                .AnyAsync(p => p.DisputeId == dispute.Id);

            if (existingPenalty)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A penalty record already exists for this dispute."
                });
            }

            dispute.AdminDecision = dto.AdminDecision.Trim();
            dispute.DecidedBy = adminId;

            // SYSTEM FAULT:
            // Do not penalize either party or move money automatically.
            // An admin can investigate and correct the payment separately.
            if (dto.FaultType == "System")
            {
                dispute.Status = "Resolved";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    status = 200,
                    message = "Dispute resolved as a system issue. No party penalty was applied.",
                    data = new
                    {
                        dispute.Id,
                        dispute.Status,
                        FaultType = dto.FaultType,
                        dispute.AdminDecision,
                        dispute.DecidedBy,
                        SaleStatus = sale.Status
                    }
                });
            }

            // If the admin decides not to apply a penalty,
            // record the decision without moving money.
            if (!dto.ApplyPenalty)
            {
                dispute.Status = "Resolved";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    status = 200,
                    message = "Dispute resolved without a penalty.",
                    data = new
                    {
                        dispute.Id,
                        dispute.Status,
                        FaultType = dto.FaultType,
                        dispute.AdminDecision,
                        dispute.DecidedBy
                    }
                });
            }

            // Calculate the penalty using the product's starting price.
            decimal productValue = product.StartingPrice;
            decimal totalPenalty = Math.Round(productValue * 0.25m, 2);
            decimal oppositePartyAmount = Math.Round(productValue * 0.20m, 2);
            decimal platformAmount = totalPenalty - oppositePartyAmount;

            // Determine the party receiving compensation.
            string oppositePartyId =
                responsibleUserId == sale.BuyerId
                    ? sale.SellerId
                    : sale.BuyerId;

            var responsibleWallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == responsibleUserId);

            if (responsibleWallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The responsible party's wallet was not found."
                });
            }

            // Sellers use SellerDeposit; buyers use BuyerFunds.
            string reservationType =
                dto.FaultType == "Seller"
                    ? "SellerDeposit"
                    : "BuyerFunds";

            var reservationQuery = _context.WalletReservations
                .Where(r =>
                    r.WalletId == responsibleWallet.Id &&
                    r.Type == reservationType &&
                    r.Status == "Reserved");

            if (reservationType == "SellerDeposit")
            {
                reservationQuery = reservationQuery
                    .Where(r => r.ProductId == sale.ProductId);
            }
            else
            {
                reservationQuery = reservationQuery
                    .Where(r => r.AuctionId == sale.AuctionId);
            }

            var reservation = await reservationQuery.FirstOrDefaultAsync();

            if (reservation == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "No eligible penalty reserve was found for the responsible party."
                });
            }

            if (reservation.Amount != totalPenalty ||
             responsibleWallet.ReservedBalance < reservation.Amount ||
             responsibleWallet.Balance < totalPenalty)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The reserved deposit must exactly match the penalty amount, and the wallet must have sufficient funds."
                });
            }

            var oppositePartyWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == oppositePartyId);

            if (oppositePartyWallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The other party's wallet was not found."
                });
            }

            var platformUser = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.UserName == "platform-system-account");

            if (platformUser == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform account was not found."
                });
            }

            var platformWallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

            if (platformWallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform wallet was not found."
                });
            }

            // If the seller is responsible, refund the buyer's payment.
            // Only refund if the sale is paid and its escrow is still reserved.
            if (dto.FaultType == "Seller" && sale.Status == "Disputed")
            {
                var escrow = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.SaleId == sale.Id &&
                        r.Type == "SaleEscrow" &&
                        r.Status == "Reserved");

                if (escrow != null)
                {
                    if (escrow.Amount != sale.FinalAmount)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "The sale escrow does not match the sale amount."
                        });
                    }

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

                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = buyerWallet.Id,
                        Type = "Credit",
                        Amount = escrow.Amount,
                        BalanceAfter = buyerWallet.Balance,
                        Reference = $"Sale refund for dispute #{dispute.Id}",
                        CreatedAt = DateTime.UtcNow
                    });

                    escrow.Status = "Released";
                    sale.Status = "Refunded";
                }
                else if (sale.PaymentSubmittedAt.HasValue)
                {
                    // Payment was made, but the escrow is no longer available.
                    // Do not issue a second refund automatically.
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Payment was previously submitted, but no active escrow was found. Admin investigation is required before applying the penalty."
                    });
                }
            }

            // Deduct the penalty from the responsible party.
            // Any excess reserve becomes available again.
            responsibleWallet.Balance -= totalPenalty;
            responsibleWallet.ReservedBalance -= reservation.Amount;
            reservation.Status = "Forfeited";

            // Pay 20% compensation to the other party.
            oppositePartyWallet.Balance += oppositePartyAmount;

            // Pay the remaining 5% to the platform.
            platformWallet.Balance += platformAmount;

            var now = DateTime.UtcNow;

            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = responsibleWallet.Id,
                Type = "Penalty",
                Amount = totalPenalty,
                BalanceAfter = responsibleWallet.Balance,
                Reference = $"Penalty for dispute #{dispute.Id}",
                CreatedAt = now
            });

            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = oppositePartyWallet.Id,
                Type = "Credit",
                Amount = oppositePartyAmount,
                BalanceAfter = oppositePartyWallet.Balance,
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

            var penalty = new Penalty
            {
                DisputeId = dispute.Id,
                AgainstUserId = responsibleUserId!,
                ProductValue = productValue,
                TotalAmount = totalPenalty,
                OppositePartyAmount = oppositePartyAmount,
                PlatformAmount = platformAmount,
                Status = "Applied"
            };

            _context.Penalties.Add(penalty);

            dispute.Status = "Resolved";

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = "Dispute resolved and penalty applied successfully.",
                data = new
                {
                    dispute.Id,
                    dispute.Status,
                    FaultType = dto.FaultType,
                    dispute.AdminDecision,
                    dispute.DecidedBy,
                    SaleStatus = sale.Status,
                    Penalty = new
                    {
                        penalty.ProductValue,
                        penalty.TotalAmount,
                        penalty.OppositePartyAmount,
                        penalty.PlatformAmount,
                        penalty.Status
                    }
                }
            });
        }

    }
}
