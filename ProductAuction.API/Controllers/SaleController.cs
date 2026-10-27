
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Sale;
using ProductAuction.API.Models;
using ProductAuction.API.Services;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SaleController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IImageUploadService _imageUploadService;

        public SaleController(AppDbContext context, IImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }

        // GET: api/sale
        // Admin views all sales.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSales()
        {
            var sales = await _context.Sales
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new SaleListDto
                {
                    Id = s.Id,
                    ProductId = s.ProductId,
                    ProductTitle = _context.Products
                        .Where(p => p.Id == s.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    BuyerId = s.BuyerId,
                    SellerId = s.SellerId,

                    // Fetch Names
                    BuyerName = _context.UserProfiles
                        .Where(p => p.UserId == s.BuyerId)
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown Buyer",
                    SellerName = _context.UserProfiles
                        .Where(p => p.UserId == s.SellerId)
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown Seller",

                    FinalAmount = s.FinalAmount,
                    CommissionAmount = s.CommissionAmount,
                    SellerProceeds = s.SellerProceeds,

                    Status = s.Status,

                    // Fetch Delivery Status
                    DeliveryStatus = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.Status)
                        .FirstOrDefault() ?? "AwaitingDispatch",

                    PaymentDeadline = s.PaymentDeadline,
                    CreatedAt = s.CreatedAt,

                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == s.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? ""
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Sales retrieved successfully.",
                data = sales
            });
        }

        // GET: api/sale/my-sales
        // View sales where the logged-in user is buyer or seller.
        [HttpGet("my-sales")]
        public async Task<IActionResult> GetMySales()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null) return Unauthorized(new { status = 401, message = "Please log in first." });

            var sales = await _context.Sales
                .Where(s => s.BuyerId == userId || s.SellerId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    Id = s.Id,
                    ProductId = s.ProductId,
                    ProductTitle = _context.Products.Where(p => p.Id == s.ProductId).Select(p => p.Title).FirstOrDefault() ?? string.Empty,
                    BuyerId = s.BuyerId,
                    SellerId = s.SellerId,
                    FinalAmount = s.FinalAmount,
                    CommissionAmount = s.CommissionAmount,
                    SellerProceeds = s.SellerProceeds,
                    Status = s.Status,
                    PaymentDeadline = s.PaymentDeadline,
                    CreatedAt = s.CreatedAt,
                    MainImageUrl = _context.ProductImages.Where(i => i.ProductId == s.ProductId && i.IsMain).Select(i => i.ImageUrl).FirstOrDefault() ?? "",

                    // Fetch DispatchedAt so frontend can calculate the 10-day window
                    DispatchedAt = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.DispatchedAt)
                        .FirstOrDefault(),

                    Dispute = _context.Disputes
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => new
                        {
                            Id = d.Id,
                            Complaint = d.Complaint,
                            EvidenceUrl = d.EvidenceUrl,
                            Status = d.Status,
                            AdminDecision = d.AdminDecision,
                            RaisedBy = d.RaisedBy,
                            CreatedAt = d.CreatedAt,
                            Penalty = _context.Penalties
                                .Where(p => p.DisputeId == d.Id)
                                .Select(p => new
                                {
                                    p.TotalAmount,
                                    p.OppositePartyAmount,
                                    p.PlatformAmount,
                                    p.AgainstUserId
                                })
                                .FirstOrDefault()
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(new { status = 200, message = "Your sales retrieved successfully.", data = sales });
        }

        // GET: api/sale/1
        // View detailed information about one sale.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSaleById(int id)
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

            var sale = await _context.Sales
                .Where(s => s.Id == id)
                .Select(s => new SaleDetailsDto
                {
                    Id = s.Id,
                    Status = s.Status,

                    FinalAmount = s.FinalAmount,
                    CommissionAmount = s.CommissionAmount,
                    SellerProceeds = s.SellerProceeds,

                    PaymentDeadline = s.PaymentDeadline,
                    PaymentSubmittedAt = s.PaymentSubmittedAt,
                    CreatedAt = s.CreatedAt,

                    BuyerId = s.BuyerId,
                    SellerId = s.SellerId,

                    ProductId = s.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == s.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault() ?? string.Empty,

                    ProductDescription = _context.Products
                        .Where(p => p.Id == s.ProductId)
                        .Select(p => p.Description)
                        .FirstOrDefault() ?? string.Empty,

                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == s.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),

                    StartingPrice = _context.Products
                        .Where(p => p.Id == s.ProductId)
                        .Select(p => p.StartingPrice)
                        .FirstOrDefault(),

                    BuyoutPrice = _context.Products
                        .Where(p => p.Id == s.ProductId)
                        .Select(p => p.BuyoutPrice)
                        .FirstOrDefault(),

                    AuctionId = s.AuctionId,

                    AuctionStatus = _context.Auctions
                        .Where(a => a.Id == s.AuctionId)
                        .Select(a => a.Status)
                        .FirstOrDefault() ?? string.Empty,

                    WinnerId = _context.Auctions
                        .Where(a => a.Id == s.AuctionId)
                        .Select(a => a.WinnerId)
                        .FirstOrDefault(),

                    BidCount = _context.Auctions
                        .Where(a => a.Id == s.AuctionId)
                        .Select(a => a.BidCount)
                        .FirstOrDefault(),

                    WinningBidAmount = _context.Bids
                        .Where(b => b.AuctionId == s.AuctionId)
                        .OrderByDescending(b => b.Amount)
                        .Select(b => (decimal?)b.Amount)
                        .FirstOrDefault(),

                    WinningBidTime = _context.Bids
                        .Where(b => b.AuctionId == s.AuctionId)
                        .OrderByDescending(b => b.Amount)
                        .Select(b => (DateTime?)b.PlacedAt)
                        .FirstOrDefault(),

                    DeliveryStatus = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.Status)
                        .FirstOrDefault(),

                    DispatchDeadline = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.DispatchDeadline)
                        .FirstOrDefault(),

                    DispatchInfo = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.DispatchInfo)
                        .FirstOrDefault(),

                    TrackingEvidence = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.TrackingEvidence)
                        .FirstOrDefault(),

                    DispatchedAt = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.DispatchedAt)
                        .FirstOrDefault(),

                    ReceiptConfirmedAt = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.ReceiptConfirmedAt)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // Only the buyer, seller, or admin may view the sale.
            if (!isAdmin && sale.BuyerId != userId && sale.SellerId != userId)
            {
                return Forbid();
            }

            return Ok(new
            {
                status = 200,
                message = "Sale details retrieved successfully.",
                data = sale
            });
        }


        // POST: api/sale/1/pay
        // The winning buyer pays for the sale from their wallet.
        [HttpPost("{id}/pay")]
        public async Task<IActionResult> PayForSale(int id)
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

            // Start a transaction so all payment changes succeed together.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id && s.BuyerId == userId);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            if (sale.Status != "AwaitingPayment")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This sale is not awaiting payment."
                });
            }

            var now = DateTime.UtcNow;

            if (now > sale.PaymentDeadline)
            {
                sale.Status = "PaymentOverdue";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return BadRequest(new
                {
                    status = 400,
                    message = "The payment deadline has passed."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet not found."
                });
            }

            // Find the buyer's eligibility reservation for this auction.
            var reservations = await _context.WalletReservations
                .Where(r =>
                    r.WalletId == wallet.Id &&
                    r.AuctionId == sale.AuctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
                .ToListAsync();

            var reservedAmount = reservations.Sum(r => r.Amount);

            // Ensure the wallet's reserved balance can cover these reservations.
            if (reservedAmount > wallet.ReservedBalance)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet reservation balance is inconsistent."
                });
            }

            // Calculate the wallet balance after paying the full sale amount.
            var newBalance = wallet.Balance - sale.FinalAmount;
            var newReservedBalance = wallet.ReservedBalance - reservedAmount;

            // Other reservations must remain covered after payment.
            if (newBalance < newReservedBalance)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Insufficient available wallet balance."
                });
            }

            // Deduct the full sale amount.
            wallet.Balance = newBalance;
            wallet.ReservedBalance = newReservedBalance;

            // Release the buyer's eligibility reservation.
            foreach (var reservation in reservations)
            {
                reservation.Status = "Released";
            }

            // Record the wallet debit.
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = "Debit",
                Amount = sale.FinalAmount,
                BalanceAfter = wallet.Balance,
                Reference = $"Sale payment #{sale.Id}",
                CreatedAt = now
            });

            // Keep the payment reserved by the system until receipt is confirmed.
            _context.WalletReservations.Add(new WalletReservation
            {
                WalletId = wallet.Id,
                ProductId = sale.ProductId,
                AuctionId = sale.AuctionId,
                SaleId = sale.Id,
                Type = "SaleEscrow",
                Amount = sale.FinalAmount,
                Status = "Reserved"
            });

            // The payment is complete; seller payout comes later.
            sale.Status = "Paid";
            sale.PaymentSubmittedAt = now;
            sale.SellerNotifiedAt = now;

            // Set the seller's dispatch deadline to five days after notification.
            _context.DeliveryRecords.Add(new DeliveryRecord
            {
                SaleId = sale.Id,
                Status = "AwaitingDispatch",
                DispatchDeadline = sale.SellerNotifiedAt.Value.AddDays(5)
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = "Payment successful. The money will be held until receipt is confirmed.",
                data = new
                {
                    sale.Id,
                    sale.FinalAmount,
                    sale.Status,
                    WalletBalance = wallet.Balance
                }
            });
        }


        // POST: api/sale/{id}/dispatch
        // The seller records dispatch after the buyer has paid.
        // POST: api/sale/{id}/dispatch
        // The seller records dispatch after the buyer has paid.
        [HttpPost("{id}/dispatch")]
        public async Task<IActionResult> DispatchSale(
            int id,
            [FromForm] DispatchSaleDto dto) // ADDED [FromForm]
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                return NotFound(new { status = 404, message = "Sale not found." });
            }

            // Only the seller can record dispatch.
            if (sale.SellerId != userId)
            {
                return Forbid();
            }

            if (sale.Status != "Paid")
            {
                return BadRequest(new { status = 400, message = "The sale must be paid before dispatch." });
            }

            if (string.IsNullOrWhiteSpace(dto.DispatchInfo) || dto.TrackingEvidenceImage == null || dto.TrackingEvidenceImage.Length == 0)
            {
                return BadRequest(new { status = 400, message = "Dispatch information and tracking evidence image are required." });
            }

            if (!sale.PaymentSubmittedAt.HasValue)
            {
                return BadRequest(new { status = 400, message = "Payment timestamp is missing." });
            }

            var now = DateTime.UtcNow;
            if (!sale.SellerNotifiedAt.HasValue)
            {
                return BadRequest(new { status = 400, message = "Seller notification timestamp is missing." });
            }

            var dispatchDeadline = sale.SellerNotifiedAt.Value.AddDays(5);

            if (now > dispatchDeadline)
            {
                return BadRequest(new { status = 400, message = "The dispatch deadline has passed." });
            }

            var delivery = await _context.DeliveryRecords
                .FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null)
            {
                delivery = new DeliveryRecord
                {
                    SaleId = sale.Id,
                    Status = "AwaitingDispatch",
                    DispatchDeadline = dispatchDeadline
                };
                _context.DeliveryRecords.Add(delivery);
            }
            else if (delivery.Status != "AwaitingDispatch")
            {
                return BadRequest(new { status = 400, message = "The delivery record is missing or dispatch has already been recorded." });
            }

            // Upload the image
            var trackingImageUrl = await _imageUploadService.UploadImageAsync(dto.TrackingEvidenceImage);

            // Update the existing delivery record.
            delivery.Status = "Dispatched";
            delivery.DispatchDeadline = dispatchDeadline;
            delivery.DispatchInfo = dto.DispatchInfo;
            delivery.TrackingEvidence = trackingImageUrl;
            delivery.DispatchedAt = now;

            // Reflect the dispatch on the Sale itself.
            sale.Status = "Dispatched";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Dispatch recorded successfully.",
                data = new
                {
                    SaleId = sale.Id,
                    DeliveryStatus = delivery.Status,
                    delivery.DispatchDeadline,
                    delivery.DispatchInfo,
                    delivery.TrackingEvidence,
                    delivery.DispatchedAt
                }
            });
        }

        // POST: api/sale/{id}/report-non-delivery
        // The seller reports that the buyer never picked up the item after
        // dispatch. Allowed only when the delivery is Dispatched and the
        // 10-day pickup window (measured from DispatchedAt) has passed.
        [HttpPost("{id}/report-non-delivery")]
        public async Task<IActionResult> ReportNonDelivery(int id)
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

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // Only the seller can report non-delivery.
            if (sale.SellerId != userId)
            {
                return Forbid();
            }

            // The sale must be in the Dispatched state.
            if (sale.Status != "Dispatched")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale must be dispatched before non-delivery can be reported."
                });
            }

            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != "Dispatched" || !delivery.DispatchedAt.HasValue)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Delivery record is missing or dispatch has not been recorded."
                });
            }

            var now = DateTime.UtcNow;
            var pickupDeadline = delivery.DispatchedAt.Value.AddDays(10);

            // The seller must wait the full 10-day pickup window before
            // they can accuse the buyer of abandoning the item.
            if (now <= pickupDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The 10-day pickup window has not yet passed. Please wait."
                });
            }

            // Prevent duplicate reports.
            if (delivery.Status == "NonDeliveryReported")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A non-delivery report has already been submitted for this sale."
                });
            }

            // Mark the delivery as non-delivery reported and lock the sale
            // so the buyer cannot confirm receipt anymore. The admin will
            // review the dispute and decide the outcome.
            delivery.Status = "NonDeliveryReported";
            sale.Status = "Disputed";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Non-delivery reported. Awaiting admin review.",
                data = new
                {
                    SaleId = sale.Id,
                    SaleStatus = sale.Status,
                    DeliveryStatus = delivery.Status,
                    delivery.DispatchedAt,
                    PickupDeadline = pickupDeadline
                }
            });
        }



        // POST: api/sale/{id}/confirm-receipt
        // The buyer confirms receipt and the system settles the sale.
        // The buyer MUST supply proof (a photo, tracking screenshot, or
        // URL) so the seller has evidence the item was received.
        [HttpPost("{id}/confirm-receipt")]
        public async Task<IActionResult> ConfirmReceipt(int id, [FromForm] ConfirmReceiptDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            // Proof of receipt is mandatory.
            if (dto.ReceiptProofImage == null || dto.ReceiptProofImage.Length == 0)
            {
                return BadRequest(new { status = 400, message = "Proof of receipt image is required." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null) return NotFound(new { status = 404, message = "Sale not found." });
            if (sale.BuyerId != userId) return Forbid();

            // Prevent duplicate settlement and block sales not awaiting receipt.
            // Once settled, the status becomes "Completed", so a second call fails here.
            // FIXED: Allow both "Paid" and "Dispatched" statuses
            if (sale.Status != "Paid" && sale.Status != "Dispatched")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale is not awaiting receipt confirmation."
                });
            }

            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != "Dispatched")
            {
                return BadRequest(new { status = 400, message = "The product must be dispatched before receipt can be confirmed." });
            }

            var escrow = await _context.WalletReservations
                .FirstOrDefaultAsync(r => r.SaleId == sale.Id && r.Type == "SaleEscrow" && r.Status == "Reserved");

            if (escrow == null || escrow.Amount != sale.FinalAmount)
            {
                return BadRequest(new { status = 400, message = "The sale escrow could not be verified." });
            }

            var commission = sale.CommissionAmount;
            var sellerProceeds = sale.SellerProceeds;

            if (commission < 0 || sellerProceeds < 0 || commission + sellerProceeds != sale.FinalAmount)
            {
                return BadRequest(new { status = 400, message = "The sale amounts do not match." });
            }

            var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);
            if (sellerWallet == null) return BadRequest(new { status = 400, message = "The seller's wallet was not found." });

            var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");
            if (platformUser == null) return BadRequest(new { status = 400, message = "The platform system account was not found." });

            var platformWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id);
            if (platformWallet == null) return BadRequest(new { status = 400, message = "The platform wallet was not found." });

            if (platformWallet.Id == sellerWallet.Id) return BadRequest(new { status = 400, message = "The platform wallet configuration is invalid." });

            // Upload the image!
            var receiptImageUrl = await _imageUploadService.UploadImageAsync(dto.ReceiptProofImage);

            var now = DateTime.UtcNow;

            // Credit seller and platform
            sellerWallet.Balance += sellerProceeds;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = sellerWallet.Id,
                Type = "Credit",
                Amount = sellerProceeds,
                BalanceAfter = sellerWallet.Balance,
                Reference = $"Sale proceeds #{sale.Id}",
                CreatedAt = now
            });

            platformWallet.Balance += commission;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = platformWallet.Id,
                Type = "Commission",
                Amount = commission,
                BalanceAfter = platformWallet.Balance,
                Reference = $"Platform commission for sale #{sale.Id}",
                CreatedAt = now
            });

            // Release seller deposit
            var sellerDeposit = await _context.WalletReservations
                .FirstOrDefaultAsync(r => r.ProductId == sale.ProductId && r.WalletId == sellerWallet.Id && r.Type == "SellerDeposit" && r.Status == "Reserved");

            // The deposit is optional: if none exists, skip this block.
            if (sellerDeposit != null)
            {
                // Graceful handling: If the balance is somehow out of sync, just clamp it to 0.
                // (This can happen if an admin manually adjusts balances).
                if (sellerWallet.ReservedBalance < sellerDeposit.Amount)
                {
                    sellerWallet.ReservedBalance = 0;
                }
                else
                {
                    // Un-reserve the money (it stays in Balance, it's just spendable again).
                    sellerWallet.ReservedBalance -= sellerDeposit.Amount;
                }

                sellerDeposit.Status = "Released";
                sellerDeposit.Status = "Released";

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = sellerWallet.Id,
                    Type = "Release",
                    Amount = sellerDeposit.Amount,
                    BalanceAfter = sellerWallet.Balance,
                    Reference = $"Seller deposit release for product #{sale.ProductId}",
                    CreatedAt = now
                });
            }

            // Release buyer reservations
            var buyerReservations = await _context.WalletReservations
                .Where(r => r.AuctionId == sale.AuctionId && r.Type == "BuyerFunds" && r.Status == "Reserved")
                .ToListAsync();

            foreach (var reservation in buyerReservations)
            {
                var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

                if (buyerWallet == null || buyerWallet.ReservedBalance < reservation.Amount)
                {
                    return BadRequest(new { status = 400, message = "A buyer reservation could not be safely released." });
                }

                buyerWallet.ReservedBalance -= reservation.Amount;
                reservation.Status = "Released";

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = buyerWallet.Id,
                    Type = "Release",
                    Amount = reservation.Amount,
                    BalanceAfter = buyerWallet.Balance,
                    Reference = $"Buyer reservation release for sale #{sale.Id}",
                    CreatedAt = now
                });
            }

            // Complete the sale
            escrow.Status = "Released";
            delivery.Status = "ReceiptConfirmed";
            delivery.ReceiptConfirmedAt = now;
            delivery.ReceiptProofUrl = receiptImageUrl; // Save the uploaded image path
            delivery.ReceiptNotes = string.IsNullOrWhiteSpace(dto.ReceiptNotes) ? null : dto.ReceiptNotes.Trim();
            sale.Status = "Completed";

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { status = 200, message = "Receipt confirmed successfully." });
        }


    }
}


