
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Sale;
using ProductAuction.API.Models;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SaleController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SaleController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/sale
        // Admin views all sales.
        [HttpGet]
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

                    FinalAmount = s.FinalAmount,
                    CommissionAmount = s.CommissionAmount,
                    SellerProceeds = s.SellerProceeds,

                    Status = s.Status,
                    PaymentDeadline = s.PaymentDeadline,
                    CreatedAt = s.CreatedAt
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

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var sales = await _context.Sales
                .Where(s => s.BuyerId == userId || s.SellerId == userId)
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

                    FinalAmount = s.FinalAmount,
                    CommissionAmount = s.CommissionAmount,
                    SellerProceeds = s.SellerProceeds,

                    Status = s.Status,
                    PaymentDeadline = s.PaymentDeadline,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Your sales retrieved successfully.",
                data = sales
            });
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
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

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

            // Set the seller's dispatch deadline.
            _context.DeliveryRecords.Add(new DeliveryRecord
            {
                SaleId = sale.Id,
                Status = "AwaitingDispatch",
                DispatchDeadline = now.AddDays(5)
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
        [HttpPost("{id}/dispatch")]
        public async Task<IActionResult> DispatchSale(
            int id,
            DispatchSaleDto dto)
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

            // Only the seller can record dispatch.
            if (sale.SellerId != userId)
            {
                return Forbid();
            }

            if (sale.Status != "Paid")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale must be paid before dispatch."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.DispatchInfo) || string.IsNullOrWhiteSpace(dto.TrackingEvidence))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Dispatch information and tracking evidence are required."
                });
            }

            if (!sale.PaymentSubmittedAt.HasValue)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Payment timestamp is missing."
                });
            }

            var now = DateTime.UtcNow;
            var dispatchDeadline = sale.PaymentSubmittedAt.Value.AddDays(5);

            if (now > dispatchDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The dispatch deadline has passed."
                });
            }

            // PayForSale creates this record when payment succeeds.
            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null ||
                delivery.Status != "AwaitingDispatch")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The delivery record is missing or dispatch has already been recorded."
                });
            }

            // Update the existing delivery record.
            delivery.Status = "Dispatched";
            delivery.DispatchDeadline = dispatchDeadline;
            delivery.DispatchInfo = dto.DispatchInfo;
            delivery.TrackingEvidence = dto.TrackingEvidence;
            delivery.DispatchedAt = now;

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


        // POST: api/sale/{id}/confirm-receipt
        // The buyer confirms receipt and the system settles the sale.
        [HttpPost("{id}/confirm-receipt")]
        public async Task<IActionResult> ConfirmReceipt(int id)
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

            // Keep all financial changes in one database transaction.
            await using var transaction = await _context.Database
                .BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

            var sale = await _context.Sales
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // Only the winning buyer can confirm receipt.
            if (sale.BuyerId != userId)
            {
                return Forbid();
            }

            // Prevent duplicate settlement and block sales not awaiting receipt.
            if (sale.Status != "Paid")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale is not awaiting receipt confirmation."
                });
            }

            // The product must have been dispatched first.
            var delivery = await _context.DeliveryRecords
                .FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != "Dispatched")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The product must be dispatched before receipt can be confirmed."
                });
            }

            // Find the reserved sale payment.
            var escrow = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.SaleId == sale.Id &&
                    r.Type == "SaleEscrow" &&
                    r.Status == "Reserved");

            if (escrow == null || escrow.Amount != sale.FinalAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale escrow could not be verified."
                });
            }

            // Verify that the recorded amounts add up to the sale amount.
            var commission = sale.CommissionAmount;
            var sellerProceeds = sale.SellerProceeds;

            if (commission < 0 ||
                sellerProceeds < 0 ||
                commission + sellerProceeds != sale.FinalAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale amounts do not match."
                });
            }

            // Find the seller's wallet.
            var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);

            if (sellerWallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The seller's wallet was not found."
                });
            }

            // Find the dedicated platform system account.
            var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");

            if (platformUser == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform system account was not found. Check that PlatformAccountSeeder has run."
                });
            }

            // Find the platform's wallet.
            var platformWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

            if (platformWallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform wallet was not found. Check that PlatformAccountSeeder has run."
                });
            }

            // The platform wallet must not be the seller's wallet.
            if (platformWallet.Id == sellerWallet.Id)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform wallet configuration is invalid."
                });
            }

            var now = DateTime.UtcNow;

            //  Credit the seller with their proceeds.
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

            //  Credit the platform with its commission.
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

            //  Release the seller's product deposit, following the
            // current policy that the deposit is refundable after a sale.
            var sellerDeposit = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == sale.ProductId &&
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved");

            if (sellerDeposit != null)
            {
                var depositWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w =>
                        w.Id == sellerDeposit.WalletId);

                if (depositWallet == null || depositWallet.ReservedBalance < sellerDeposit.Amount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The seller deposit could not be safely released."
                    });
                }

                depositWallet.ReservedBalance -= sellerDeposit.Amount;
                sellerDeposit.Status = "Released";

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = depositWallet.Id,
                    Type = "Release",
                    Amount = sellerDeposit.Amount,
                    BalanceAfter = depositWallet.Balance,
                    Reference = $"Seller deposit release for product #{sale.ProductId}",
                    CreatedAt = now
                });
            }

            // Release the buyer's remaining eligibility reservations
            var buyerReservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == sale.AuctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
                .ToListAsync();

            foreach (var reservation in buyerReservations)
            {
                var buyerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

                if (buyerWallet == null || buyerWallet.ReservedBalance < reservation.Amount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "A buyer reservation could not be safely released."
                    });
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

            // Close the escrow reservation.
            escrow.Status = "Released";

            //  Mark delivery and sale as completed.
            delivery.Status = "ReceiptConfirmed";
            delivery.ReceiptConfirmedAt = now;
            sale.Status = "Completed";

            // Save all financial and status changes together.
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = "Receipt confirmed. Seller proceeds and platform commission have been credited.",
                data = new
                {
                    SaleId = sale.Id,
                    SaleStatus = sale.Status,
                    DeliveryStatus = delivery.Status,
                    SellerProceeds = sellerProceeds,
                    PlatformCommission = commission,
                    SellerWalletBalance = sellerWallet.Balance,
                    PlatformWalletBalance = platformWallet.Balance,
                    SellerDepositReleased = sellerDeposit != null,
                    BuyerReservationsReleased = buyerReservations.Count
                }
            });
        }
    }
}
