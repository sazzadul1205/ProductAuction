using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
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
            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional sale-status filter (AwaitingPayment / Paid / Dispatched / etc.)
            var filter = query.Filter;

            // status filter
            var deliveryStatus = query.DeliveryStatus;

            // _logger.LogInformation("GetSales called: page={Page}, pageSize={PageSize}, filter={Filter}, deliveryStatus={DeliveryStatus}, search={Search}.",
            //     page, pageSize, filter, deliveryStatus, search);

            var salesQuery = _context.Sales.AsQueryable();

            // apply sale-status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                // "In Progress" is a frontend convenience for Paid | Dispatched
                if (filter == "InProgress")
                {
                    salesQuery = salesQuery.Where(s =>
                        s.Status == "Paid" || s.Status == "Dispatched");
                }
                else if (filter == "Awaiting Payment")
                {
                    salesQuery = salesQuery.Where(s =>
                        s.Status == "AwaitingPayment" || s.Status == "PaymentOverdue");
                }
                else if (filter == "Completed")
                {
                    salesQuery = salesQuery.Where(s => s.Status == "Completed");
                }
                else if (filter == "Disputed")
                {
                    salesQuery = salesQuery.Where(s =>
                        s.Status == "Disputed" || s.Status == "Refunded");
                }
                else
                {
                    salesQuery = salesQuery.Where(s => s.Status == filter);
                }
            }

            // apply delivery-status filter
            if (!string.IsNullOrWhiteSpace(deliveryStatus) && deliveryStatus != "All")
            {
                salesQuery = salesQuery.Where(s =>
                    _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.Status)
                        .FirstOrDefault() == deliveryStatus);
            }

            // apply search filter , buyer name, or seller name
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

                // _logger.LogDebug("GetSales: search '{Term}' matched {ProductCount} product(s) and {UserCount} user(s).",
                //     term, matchingProductIds.Count, matchingUserIds.Count);
            }

            salesQuery = salesQuery.OrderByDescending(s => s.CreatedAt);

            var totalCount = await salesQuery.CountAsync();

            // Global stats
            var totalSalesCount = await _context.Sales.CountAsync();
            var awaitingPaymentCount = await _context.Sales.CountAsync(s =>
                s.Status == "AwaitingPayment" || s.Status == "PaymentOverdue");
            var activeDeliveriesCount = await _context.Sales.CountAsync(s =>
                s.Status == "Paid" || s.Status == "Dispatched");
            var completedCount = await _context.Sales.CountAsync(s =>
                s.Status == "Completed");

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
                    Status = s.Status,

                    DeliveryStatus = _context.DeliveryRecords
                        .Where(d => d.SaleId == s.Id)
                        .Select(d => d.Status)
                        .FirstOrDefault() ?? "AwaitingDispatch",

                    PaymentDeadline = s.PaymentDeadline,
                    CreatedAt = s.CreatedAt,

                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == s.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // _logger.LogInformation("GetSales returned {Count} of {Total} sale(s) (totalSales={TotalSales}, awaitingPayment={Awaiting}, activeDeliveries={Active}, completed={Completed}).",
            //     sales.Count, totalCount, totalSalesCount, awaitingPaymentCount, activeDeliveriesCount, completedCount);

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

            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional sale-status filter
            var filter = query.Filter;

            // _logger.LogInformation("GetMySales called by {UserId}: page={Page}, pageSize={PageSize}, filter={Filter}, search={Search}.",
            //     userId, page, pageSize, filter, search);

            var salesQuery = _context.Sales
                .Where(s => s.BuyerId == userId || s.SellerId == userId)
                .AsQueryable();

            // apply sale-status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                if (filter == "Awaiting Payment")
                {
                    salesQuery = salesQuery.Where(s =>
                        s.Status == "AwaitingPayment" || s.Status == "PaymentOverdue");
                }
                else if (filter == "Awaiting Dispatch")
                {
                    salesQuery = salesQuery.Where(s => s.Status == "Paid");
                }
                else if (filter == "Dispatched")
                {
                    salesQuery = salesQuery.Where(s => s.Status == "Dispatched");
                }
                else if (filter == "Completed")
                {
                    salesQuery = salesQuery.Where(s => s.Status == "Completed");
                }
                else if (filter == "Disputed")
                {
                    salesQuery = salesQuery.Where(s =>
                        s.Status == "Disputed" || s.Status == "Refunded");
                }
                else
                {
                    salesQuery = salesQuery.Where(s => s.Status == filter);
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

                salesQuery = salesQuery.Where(s =>
                    matchingProductIds.Contains(s.ProductId));

                // _logger.LogDebug("GetMySales: search '{Term}' matched {Count} product(s).", term, matchingProductIds.Count);
            }

            salesQuery = salesQuery.OrderByDescending(s => s.CreatedAt);

            var totalCount = await salesQuery.CountAsync();

            // Global counts
            var totalSalesCount = await _context.Sales.CountAsync(s =>
                s.BuyerId == userId || s.SellerId == userId);
            var awaitingDispatchCount = await _context.Sales.CountAsync(s =>
                (s.BuyerId == userId || s.SellerId == userId) && s.Status == "Paid");
            var totalRevenue = await _context.Sales
                .Where(s => (s.BuyerId == userId || s.SellerId == userId)
                    && (s.Status == "Completed" || s.Status == "Dispatched" || s.Status == "Paid"))
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
                    s.Status,
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
                            d.Status,
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

            // _logger.LogInformation("GetMySales returned {Count} of {Total} sale(s) for {UserId} (awaitingDispatch={Awaiting}, revenue={Revenue}).",
            //     sales.Count, totalCount, userId, awaitingDispatchCount, totalRevenue);

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
        // Money leaves the buyer's balance and is held in escrow until receipt is confirmed.
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

            // Start Transaction
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

            // if the deadline has already passed, flip it to overdue and stop.
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

            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId);

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
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
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

            // figure out balance after paying
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
                reservation.Status = "Released";
            }

            // record the wallet debit
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = "Debit",
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
                Type = "SaleEscrow",
                Amount = sale.FinalAmount,
                Status = "Reserved"
            });

            // mark the sale as paid
            sale.Status = "Paid";
            sale.PaymentSubmittedAt = now;
            sale.SellerNotifiedAt = now;

            // seller has 5 days to dispatch
            _context.DeliveryRecords.Add(new DeliveryRecord
            {
                SaleId = sale.Id,
                Status = "AwaitingDispatch",
                DispatchDeadline = sale.SellerNotifiedAt.Value.AddDays(5)
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // _logger.LogInformation("PayForSale: sale #{SaleId} paid ({Amount}); escrow created; seller notified at {NotifiedAt}.",
            //     id, sale.FinalAmount, now);

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

            if (sale.Status != "Paid")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale must be paid before dispatch."
                });
            }

            if (string.IsNullOrEmpty(dto.DispatchInfo) ||
                dto.TrackingEvidenceImage == null ||
                dto.TrackingEvidenceImage.Length == 0)
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
                    Status = "AwaitingDispatch",
                    DispatchDeadline = dispatchDeadline
                };

                _context.DeliveryRecords.Add(delivery);
            }
            else if (delivery.Status != "AwaitingDispatch")
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
            delivery.Status = "Dispatched";
            delivery.DispatchDeadline = dispatchDeadline;
            delivery.DispatchInfo = dto.DispatchInfo;
            delivery.TrackingEvidence = trackingImageUrl;
            delivery.DispatchedAt = now;

            // reflect dispatch on the sale itself
            sale.Status = "Dispatched";

            await _context.SaveChangesAsync();

            // _logger.LogInformation("DispatchSale: sale #{SaleId} dispatched (info='{Info}', evidenceUrl='{Evidence}').",
            //     id, delivery.DispatchInfo, delivery.TrackingEvidence);

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
        // Seller reports that the buyer never picked the item up.
        // Only allowed after the 10-day pickup window (from DispatchedAt) closes.
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

            // seller has to wait the full 10 days before accusing the buyer
            if (now <= pickupDeadline)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The 10-day pickup window has not yet passed. Please wait."
                });
            }

            // no duplicate reports
            if (delivery.Status == "NonDeliveryReported")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A non-delivery report has already been submitted for this sale."
                });
            }

            // lock the sale so the buyer can't confirm receipt now 
            delivery.Status = "NonDeliveryReported";
            sale.Status = "Disputed";

            await _context.SaveChangesAsync();

            // _logger.LogInformation("ReportNonDelivery: sale #{SaleId} reported for non-delivery; awaiting admin review.", id);

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
        // Buyer confirms receipt — the sale settles, seller gets paid, platform gets commission.
        // Proof image is required so the seller has evidence the item was received.
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

            // proof of receipt is mandatory
            if (dto.ReceiptProofImage == null || dto.ReceiptProofImage.Length == 0)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Proof of receipt image is required."
                });
            }

            // _logger.LogInformation("ConfirmReceipt called by user {UserId} for sale #{SaleId}.", userId, id);

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

            // can only confirm receipt on a Paid or Dispatched sale.
            if (sale.Status != "Paid" && sale.Status != "Dispatched")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The sale is not awaiting receipt confirmation."
                });
            }

            // the product must actually have been shipped
            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != "Dispatched")
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

            // safety check — the platform wallet and seller wallet must be different
            if (platformWallet.Id == sellerWallet.Id)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "The platform wallet configuration is invalid."
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
                Type = "Credit",
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
                Type = "Commission",
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
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved");

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

                // _logger.LogDebug("ConfirmReceipt: seller deposit of {Amount} released for sale #{SaleId}.",
                //     sellerDeposit.Amount, id);
            }

            // release every buyer's eligibility reservation for this auction.
            var buyerReservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == sale.AuctionId &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
                .ToListAsync();

            foreach (var reservation in buyerReservations)
            {
                var buyerWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

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

            escrow.Status = "Released";
            delivery.Status = "ReceiptConfirmed";
            delivery.ReceiptConfirmedAt = now;
            delivery.ReceiptProofUrl = receiptImageUrl;
            delivery.ReceiptNotes = string.IsNullOrEmpty(dto.ReceiptNotes) ? null : dto.ReceiptNotes.Trim();
            sale.Status = "Completed";

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // _logger.LogInformation("ConfirmReceipt: sale #{SaleId} completed; seller paid {SellerProceeds}, platform {Commission}.",
            //     id, sellerProceeds, commission);

            return Ok(new
            {
                status = 200,
                message = "Receipt confirmed successfully."
            });
        }
    }
}