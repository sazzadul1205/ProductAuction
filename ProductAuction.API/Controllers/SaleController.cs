using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Sale;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;
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
        private readonly ILogger<SaleController> _logger;

        public SaleController(
            AppDbContext context,
            IImageUploadService imageUploadService,
            ILogger<SaleController> logger)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _logger = logger;
        }


        // GET: api/sale
        // Admin view of every sale.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSales([FromQuery] SalesQueryDto query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

            var search = query.Search;
            var filter = query.Filter;
            var deliveryStatus = query.DeliveryStatus;

            var salesQuery = _context.Sales.AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "InProgress")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Paid || s.Status == SaleStatus.Dispatched);
                }
                else if (filter == "Awaiting Payment")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.AwaitingPayment || s.Status == SaleStatus.PaymentOverdue);
                }
                else if (filter == "Completed")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Completed);
                }
                else if (filter == "Disputed")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Disputed || s.Status == SaleStatus.Refunded);
                }
                else if (Enum.TryParse<SaleStatus>(filter, ignoreCase: true, out var parsedStatus))
                {
                    salesQuery = salesQuery.Where(s => s.Status == parsedStatus);
                }
            }

            if (!string.IsNullOrWhiteSpace(deliveryStatus) && deliveryStatus != "All"
                && Enum.TryParse<DeliveryStatus>(deliveryStatus, ignoreCase: true, out var parsedDelivery))
            {
                salesQuery = salesQuery.Where(s =>
                    _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.Status)
                        .FirstOrDefault() == parsedDelivery);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                var matchingUserIds = await _context.UserProfiles
                    .Where(p => p.FullName.Contains(term))
                    .Select(p => p.UserId)
                    .ToListAsync();

                salesQuery = salesQuery.Where(s =>
                    matchingProductIds.Contains(s.ProductId) ||
                    matchingUserIds.Contains(s.BuyerId) ||
                    matchingUserIds.Contains(s.SellerId));
            }

            salesQuery = salesQuery.OrderByDescending(s => s.CreatedAt);

            var totalCount = await salesQuery.CountAsync();

            var totalSalesCount = await _context.Sales.CountAsync();
            var awaitingPaymentCount = await _context.Sales.CountAsync(s =>
                s.Status == SaleStatus.AwaitingPayment || s.Status == SaleStatus.PaymentOverdue);
            var activeDeliveriesCount = await _context.Sales.CountAsync(s =>
                s.Status == SaleStatus.Paid || s.Status == SaleStatus.Dispatched);
            var completedCount = await _context.Sales.CountAsync(s =>
                s.Status == SaleStatus.Completed);

            var sales = await salesQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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
                    Status = s.Status.ToString(),

                    DeliveryStatus = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.Status.ToString())
                        .FirstOrDefault() ?? "AwaitingDispatch",

                    PaymentDeadline = s.PaymentDeadline,
                    CreatedAt = s.CreatedAt,

                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == s.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Sales retrieved successfully.",
                data = new
                {
                    items = sales,
                    page,
                    pageSize,
                    totalCount,
                    totalSalesCount,
                    awaitingPaymentCount,
                    activeDeliveriesCount,
                    completedCount
                }
            });
        }


        // GET: api/sale/my-sales
        // Sales where the logged-in user is either the buyer or the seller.
        [HttpGet("my-sales")]
        public async Task<IActionResult> GetMySales([FromQuery] PaginationQueryDto query)
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

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);
            var search = query.Search;
            var filter = query.Filter;

            var salesQuery = _context.Sales
                .Where(s => s.BuyerId == userId || s.SellerId == userId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "Awaiting Payment")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.AwaitingPayment || s.Status == SaleStatus.PaymentOverdue);
                }
                else if (filter == "Awaiting Dispatch")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Paid);
                }
                else if (filter == "Dispatched")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Dispatched);
                }
                else if (filter == "Completed")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Completed);
                }
                else if (filter == "Disputed")
                {
                    salesQuery = salesQuery.Where(s => s.Status == SaleStatus.Disputed || s.Status == SaleStatus.Refunded);
                }
                else if (Enum.TryParse<SaleStatus>(filter, ignoreCase: true, out var parsedStatus))
                {
                    salesQuery = salesQuery.Where(s => s.Status == parsedStatus);
                }
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                salesQuery = salesQuery.Where(s => matchingProductIds.Contains(s.ProductId));
            }

            salesQuery = salesQuery.OrderByDescending(s => s.CreatedAt);

            var totalCount = await salesQuery.CountAsync();

            var totalSalesCount = await _context.Sales.CountAsync(s => s.BuyerId == userId || s.SellerId == userId);
            var awaitingDispatchCount = await _context.Sales.CountAsync(s => (s.BuyerId == userId || s.SellerId == userId) && s.Status == SaleStatus.Paid);
            var totalRevenue = await _context.Sales
                .Where(s => (s.BuyerId == userId || s.SellerId == userId)
                    && (s.Status == SaleStatus.Completed || s.Status == SaleStatus.Dispatched || s.Status == SaleStatus.Paid))
                .SumAsync(s => s.SellerProceeds);

            var sales = await salesQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == s.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),

                    s.BuyerId,
                    s.SellerId,
                    s.FinalAmount,
                    s.CommissionAmount,
                    s.SellerProceeds,
                    Status = s.Status.ToString(),
                    s.PaymentDeadline,
                    s.CreatedAt,

                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == s.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ?? "",

                    DispatchedAt = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.DispatchedAt)
                        .FirstOrDefault(),

                    Dispute = _context.Disputes
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => new
                        {
                            d.Id,
                            d.Complaint,
                            d.EvidenceUrl,
                            Status = d.Status.ToString(),
                            d.AdminDecision,
                            d.RaisedBy,
                            d.CreatedAt,

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

            return Ok(new
            {
                status = 200,
                message = "Your sales retrieved successfully.",
                data = new
                {
                    items = sales,
                    page,
                    pageSize,
                    totalCount,
                    totalSalesCount,
                    awaitingDispatchCount,
                    totalRevenue
                }
            });
        }


        // POST: api/sale/{id}/pay
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

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id && s.BuyerId == userId);

            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // Start Transaction
            await using var transaction = await _context.Database.BeginTransactionAsync();

            if (sale.Status != SaleStatus.AwaitingPayment)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This sale is not awaiting payment."
                });
            }

            var now = DateTime.UtcNow;

            // if the deadline has already passed, change it to overdue and stop.
            if (now > sale.PaymentDeadline)
            {
                sale.Status = SaleStatus.PaymentOverdue;

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

            // find the buyer's eligibility reservations for this auction
            var reservations = await _context.WalletReservations
                .Where(r =>
                    r.WalletId == wallet.Id &&
                    r.AuctionId == sale.AuctionId &&
                    r.Type == ReservationType.BuyerFunds &&
                    r.Status == ReservationStatus.Reserved)
                .ToListAsync();

            var reservedAmount = reservations.Sum(r => r.Amount);

            // reserved balance should cover these reservations.
            if (reservedAmount > wallet.ReservedBalance)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet reservation balance is inconsistent."
                });
            }

            var newBalance = wallet.Balance - sale.FinalAmount;
            var newReservedBalance = wallet.ReservedBalance - reservedAmount;

            // never allow the wallet balance to go negative
            if (newBalance < 0)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Insufficient wallet balance."
                });
            }

            // the buyer must still be able to cover their OTHER active reservations
            if (newBalance < newReservedBalance)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Insufficient available wallet balance."
                });
            }

            // deduct the full sale amount
            wallet.Balance = newBalance;
            wallet.ReservedBalance = newReservedBalance;

            // release the buyer's eligibility reservations for this auction 
            foreach (var reservation in reservations)
            {
                reservation.Status = ReservationStatus.Released;
            }

            // record the wallet debit
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.Debit,
                Amount = sale.FinalAmount,
                BalanceAfter = wallet.Balance,
                Reference = $"Sale payment #{sale.Id}",
                CreatedAt = now
            });

            // lock the money in escrow until receipt is confirmed.
            _context.WalletReservations.Add(new WalletReservation
            {
                WalletId = wallet.Id,
                ProductId = sale.ProductId,
                AuctionId = sale.AuctionId,
                SaleId = sale.Id,
                Type = ReservationType.SaleEscrow,
                Amount = sale.FinalAmount,
                Status = ReservationStatus.Reserved
            });

            // mark the sale as paid
            sale.Status = SaleStatus.Paid;
            sale.PaymentSubmittedAt = now;
            sale.SellerNotifiedAt = now;

            // seller has 5 days to dispatch
            _context.DeliveryRecords.Add(new DeliveryRecord
            {
                SaleId = sale.Id,
                Status = DeliveryStatus.AwaitingDispatch,
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
                    Status = sale.Status.ToString(),
                    WalletBalance = wallet.Balance
                }
            });
        }

        // POST: api/sale/{id}/dispatch
        // The seller records dispatch after the buyer has paid.
        [HttpPost("{id}/dispatch")]
        public async Task<IActionResult> DispatchSale(int id, [FromForm] DispatchSaleDto dto)
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

            // only the seller can record dispatch
            if (sale.SellerId != userId)
            {
                return Forbid();
            }

            if (sale.Status != SaleStatus.Paid)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale must be paid before dispatch."
                });
            }

            if (string.IsNullOrEmpty(dto.DispatchInfo) || dto.TrackingEvidenceImage == null || dto.TrackingEvidenceImage.Length == 0)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Dispatch information and tracking evidence image are required."
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

            if (!sale.SellerNotifiedAt.HasValue)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Seller notification timestamp is missing."
                });
            }

            var now = DateTime.UtcNow;
            var dispatchDeadline = sale.SellerNotifiedAt.Value.AddDays(5);
            if (now > dispatchDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The dispatch deadline has passed."
                });
            }

            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);
            if (delivery == null)
            {
                delivery = new DeliveryRecord
                {
                    SaleId = sale.Id,
                    Status = DeliveryStatus.AwaitingDispatch,
                    DispatchDeadline = dispatchDeadline
                };

                _context.DeliveryRecords.Add(delivery);
            }
            else if (delivery.Status != DeliveryStatus.AwaitingDispatch)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The delivery record is missing or dispatch has already been recorded."
                });
            }

            // upload the tracking image
            var trackingImageUrl = await _imageUploadService.UploadImageAsync(dto.TrackingEvidenceImage);

            // update the delivery record
            delivery.Status = DeliveryStatus.Dispatched;
            delivery.DispatchDeadline = dispatchDeadline;
            delivery.DispatchInfo = dto.DispatchInfo;
            delivery.TrackingEvidence = trackingImageUrl;
            delivery.DispatchedAt = now;

            // reflect dispatch on the sale itself
            sale.Status = SaleStatus.Dispatched;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Dispatch recorded successfully.",
                data = new
                {
                    SaleId = sale.Id,
                    DeliveryStatus = delivery.Status.ToString(),
                    delivery.DispatchDeadline,
                    delivery.DispatchInfo,
                    delivery.TrackingEvidence,
                    delivery.DispatchedAt
                }
            });
        }


        // POST: api/sale/{id}/report-non-delivery
        // Seller reports that the buyer never picked the item up.
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

            // only the seller can report non-delivery
            if (sale.SellerId != userId)
            {
                return Forbid();
            }

            // sale must actually be in the Dispatched state
            if (sale.Status != SaleStatus.Dispatched)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale must be dispatched before non-delivery can be reported."
                });
            }

            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != DeliveryStatus.Dispatched || !delivery.DispatchedAt.HasValue)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Delivery record is missing or dispatch has not been recorded."
                });
            }

            var now = DateTime.UtcNow;
            var pickupDeadline = delivery.DispatchedAt.Value.AddDays(10);
            if (now <= pickupDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The 10-day pickup window has not yet passed. Please wait."
                });
            }

            if (delivery.Status == DeliveryStatus.NonDeliveryReported)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A non-delivery report has already been submitted for this sale."
                });
            }

            // lock the sale so the buyer can't confirm receipt now 
            delivery.Status = DeliveryStatus.NonDeliveryReported;
            sale.Status = SaleStatus.Disputed;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Non-delivery reported. Awaiting admin review.",
                data = new
                {
                    SaleId = sale.Id,
                    SaleStatus = sale.Status.ToString(),
                    DeliveryStatus = delivery.Status.ToString(),
                    delivery.DispatchedAt,
                    PickupDeadline = pickupDeadline
                }
            });
        }


        // POST: api/sale/{id}/confirm-receipt
        // Buyer confirms receipt
        [HttpPost("{id}/confirm-receipt")]
        public async Task<IActionResult> ConfirmReceipt(int id, [FromForm] ConfirmReceiptDto dto)
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

            if (dto.ReceiptProofImage == null || dto.ReceiptProofImage.Length == 0)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Proof of receipt image is required."
                });
            }

            // Start Transaction
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id);
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            if (sale.BuyerId != userId)
            {
                return Forbid();
            }

            if (sale.Status != SaleStatus.Paid && sale.Status != SaleStatus.Dispatched)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale is not awaiting receipt confirmation."
                });
            }

            // the product must actually have been shipped
            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);
            if (delivery == null || delivery.Status != DeliveryStatus.Dispatched)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The product must be dispatched before receipt can be confirmed."
                });
            }

            // escrow has to be there and match the sale amount.
            var escrow = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.SaleId == sale.Id &&
                    r.Type == ReservationType.SaleEscrow &&
                    r.Status == ReservationStatus.Reserved);

            if (escrow == null || escrow.Amount != sale.FinalAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale escrow could not be verified."
                });
            }

            var commission = sale.CommissionAmount;
            var sellerProceeds = sale.SellerProceeds;

            // these two must add up to the final amount
            if (commission < 0 || sellerProceeds < 0 || commission + sellerProceeds != sale.FinalAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale amounts do not match."
                });
            }

            var sellerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sale.SellerId);

            if (sellerWallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The seller's wallet was not found."
                });
            }

            var platformUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "platform-system-account");
            if (platformUser == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform system account was not found."
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

            // upload the proof image
            var receiptImageUrl = await _imageUploadService.UploadImageAsync(dto.ReceiptProofImage);

            var now = DateTime.UtcNow;

            // pay the seller
            sellerWallet.Balance += sellerProceeds;

            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = sellerWallet.Id,
                Type = WalletTransactionType.Credit,
                Amount = sellerProceeds,
                BalanceAfter = sellerWallet.Balance,
                Reference = $"Sale proceeds #{sale.Id}",
                CreatedAt = now
            });

            // pay the platform
            platformWallet.Balance += commission;

            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = platformWallet.Id,
                Type = WalletTransactionType.Commission,
                Amount = commission,
                BalanceAfter = platformWallet.Balance,
                Reference = $"Platform commission for sale #{sale.Id}",
                CreatedAt = now
            });

            // release the seller's deposit
            var sellerDeposit = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == sale.ProductId &&
                    r.WalletId == sellerWallet.Id &&
                    r.Type == ReservationType.SellerDeposit &&
                    r.Status == ReservationStatus.Reserved);

            if (sellerDeposit != null)
            {
                if (sellerWallet.ReservedBalance < sellerDeposit.Amount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Seller wallet reserved balance is inconsistent with the reserved deposit."
                    });
                }

                // un-reserve 
                sellerWallet.ReservedBalance -= sellerDeposit.Amount;

                sellerDeposit.Status = ReservationStatus.Released;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = sellerWallet.Id,
                    Type = WalletTransactionType.Release,
                    Amount = sellerDeposit.Amount,
                    BalanceAfter = sellerWallet.Balance,
                    Reference = $"Seller deposit release for product #{sale.ProductId}",
                    CreatedAt = now
                });
            }

            // release every buyer's eligibility reservation for this auction.
            var buyerReservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == sale.AuctionId &&
                    r.Type == ReservationType.BuyerFunds &&
                    r.Status == ReservationStatus.Reserved)
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
                reservation.Status = ReservationStatus.Released;

                _context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = buyerWallet.Id,
                    Type = WalletTransactionType.Release,
                    Amount = reservation.Amount,
                    BalanceAfter = buyerWallet.Balance,
                    Reference = $"Buyer reservation release for sale #{sale.Id}",
                    CreatedAt = now
                });
            }

            escrow.Status = ReservationStatus.Released;
            delivery.Status = DeliveryStatus.ReceiptConfirmed;
            delivery.ReceiptConfirmedAt = now;
            delivery.ReceiptProofUrl = receiptImageUrl;
            delivery.ReceiptNotes = string.IsNullOrEmpty(dto.ReceiptNotes) ? null : dto.ReceiptNotes.Trim();
            sale.Status = SaleStatus.Completed;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = "Receipt confirmed successfully."
            });
        }
    }
}