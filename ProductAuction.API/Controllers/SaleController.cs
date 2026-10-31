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
            // Normalize the page number — never below 1
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size — default 20, hard cap at 100
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Free-text search term (may be null or empty)
            var search = query.Search;

            // Optional sale-status filter (AwaitingPayment / Paid / Dispatched / etc.)
            var filter = query.Filter;

            // Optional delivery-status filter
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

            // apply search filter — matches product title, buyer name, or seller name
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

            // Global card stats — counted across ALL sales, unaffected by
            // search/delivery/status filters or pagination.
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


            // System.Diagnostics.Debug.Assert(sales.Count <= pageSize);
            // System.Diagnostics.Debug.Assert(totalSalesCount >= totalCount);

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
                // _logger.LogWarning("GetMySales: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // Normalize the page number — never below 1
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size — default 20, hard cap at 100
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Free-text search term 
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

            // apply search filter — matches product title
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

            // Global counts across ALL of this user's sales.
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


            // System.Diagnostics.Debug.Assert(sales.Count <= pageSize);
            // System.Diagnostics.Debug.Assert(sales.All(s => s.BuyerId == userId || s.SellerId == userId));

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
        [HttpPost("{id}/pay")]
        public async Task<IActionResult> PayForSale(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                // _logger.LogWarning("PayForSale: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // _logger.LogInformation("PayForSale called by user {UserId} for sale #{SaleId}.", userId, id);

            // all payment changes below need to succeed together
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id && s.BuyerId == userId);

            if (sale == null)
            {
                // _logger.LogWarning("PayForSale: sale #{SaleId} not found for buyer {UserId}.", id, userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            if (sale.Status != "AwaitingPayment")
            {
                // _logger.LogWarning("PayForSale: sale #{SaleId} has status {Status}; expected AwaitingPayment.", id, sale.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "This sale is not awaiting payment."
                });
            }

            var now = DateTime.UtcNow;

            // if the deadline has already passed, flip it to overdue and stop
            if (now > sale.PaymentDeadline)
            {
                sale.Status = "PaymentOverdue";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // _logger.LogWarning("PayForSale: sale #{SaleId} marked PaymentOverdue (deadline passed at {Deadline}).", id, sale.PaymentDeadline);

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
                // _logger.LogError("PayForSale: wallet for user {UserId} not found.", userId);

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

            // sanity check: reserved balance should cover these reservations
            if (reservedAmount > wallet.ReservedBalance)
            {
                // _logger.LogError("PayForSale: reservation total {ReservedAmount} exceeds wallet reserved balance {ReservedBalance} for sale #{SaleId}.",
                //     reservedAmount, wallet.ReservedBalance, id);

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
                // _logger.LogWarning("PayForSale: insufficient balance for user {UserId} on sale #{SaleId} (balance={Balance}, amount={Amount}).",
                //     userId, wallet.Balance, sale.FinalAmount);

                return BadRequest(new
                {
                    status = 400,
                    message = "Insufficient wallet balance."
                });
            }

            // other reservations must remain covered after payment
            if (newBalance < newReservedBalance)
            {
                // _logger.LogWarning("PayForSale: available balance would fall below remaining reservations on sale #{SaleId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Insufficient available wallet balance."
                });
            }

            // deduct the full sale amount
            wallet.Balance = newBalance;
            wallet.ReservedBalance = newReservedBalance;

            // release the buyer's eligibility reservations
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

            // lock the money in escrow until receipt is confirmed
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


            // System.Diagnostics.Debug.Assert(sale.Status == "Paid");
            // System.Diagnostics.Debug.Assert(wallet.Balance == newBalance && wallet.ReservedBalance == newReservedBalance);

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
                // _logger.LogWarning("DispatchSale: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // _logger.LogInformation("DispatchSale called by user {UserId} for sale #{SaleId}.", userId, id);

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                // _logger.LogWarning("DispatchSale: sale #{SaleId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // only the seller can record dispatch
            if (sale.SellerId != userId)
            {
                // _logger.LogWarning("DispatchSale: user {UserId} is not the seller of sale #{SaleId}.", userId, id);

                return Forbid();
            }

            if (sale.Status != "Paid")
            {
                // _logger.LogWarning("DispatchSale: sale #{SaleId} has status {Status}; expected Paid.", id, sale.Status);

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
                // _logger.LogWarning("DispatchSale: dispatch info or tracking evidence missing for sale #{SaleId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Dispatch information and tracking evidence image are required."
                });
            }

            if (!sale.PaymentSubmittedAt.HasValue)
            {
                // _logger.LogError("DispatchSale: payment timestamp missing on sale #{SaleId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Payment timestamp is missing."
                });
            }

            if (!sale.SellerNotifiedAt.HasValue)
            {
                // _logger.LogError("DispatchSale: seller notification timestamp missing on sale #{SaleId}.", id);

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
                // _logger.LogWarning("DispatchSale: dispatch deadline {Deadline} passed for sale #{SaleId}.", dispatchDeadline, id);

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
                // _logger.LogWarning("DispatchSale: delivery #{DeliveryId} already in status {Status} for sale #{SaleId}.",
                //     delivery.Id, delivery.Status, id);

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


            // System.Diagnostics.Debug.Assert(sale.Status == "Dispatched");
            // System.Diagnostics.Debug.Assert(delivery.DispatchedAt.HasValue);

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
                // _logger.LogWarning("ReportNonDelivery: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // _logger.LogInformation("ReportNonDelivery called by user {UserId} for sale #{SaleId}.", userId, id);

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                // _logger.LogWarning("ReportNonDelivery: sale #{SaleId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            // only the seller can report non-delivery
            if (sale.SellerId != userId)
            {
                // _logger.LogWarning("ReportNonDelivery: user {UserId} is not the seller of sale #{SaleId}.", userId, id);

                return Forbid();
            }

            // sale must actually be in the Dispatched state
            if (sale.Status != "Dispatched")
            {
                // _logger.LogWarning("ReportNonDelivery: sale #{SaleId} has status {Status}; expected Dispatched.", id, sale.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "The sale must be dispatched before non-delivery can be reported."
                });
            }

            var delivery = await _context.DeliveryRecords.FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != "Dispatched" || !delivery.DispatchedAt.HasValue)
            {
                // _logger.LogWarning("ReportNonDelivery: delivery record missing or not Dispatched for sale #{SaleId}.", id);

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
                // _logger.LogWarning("ReportNonDelivery: pickup window still open for sale #{SaleId} until {Deadline}.",
                //     id, pickupDeadline);

                return BadRequest(new
                {
                    status = 400,
                    message = "The 10-day pickup window has not yet passed. Please wait."
                });
            }

            // no duplicate reports
            if (delivery.Status == "NonDeliveryReported")
            {
                // _logger.LogWarning("ReportNonDelivery: non-delivery already reported for sale #{SaleId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "A non-delivery report has already been submitted for this sale."
                });
            }

            // lock the sale so the buyer can't confirm receipt now —
            // the admin needs to review and decide
            delivery.Status = "NonDeliveryReported";
            sale.Status = "Disputed";

            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(sale.Status == "Disputed");
            // System.Diagnostics.Debug.Assert(delivery.Status == "NonDeliveryReported");

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
                // _logger.LogWarning("ConfirmReceipt: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // proof of receipt is mandatory
            if (dto.ReceiptProofImage == null || dto.ReceiptProofImage.Length == 0)
            {
                // _logger.LogWarning("ConfirmReceipt: receipt proof image missing for sale #{SaleId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Proof of receipt image is required."
                });
            }

            // _logger.LogInformation("ConfirmReceipt called by user {UserId} for sale #{SaleId}.", userId, id);

            // all the wallet changes below need to succeed together
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                // _logger.LogWarning("ConfirmReceipt: sale #{SaleId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Sale not found."
                });
            }

            if (sale.BuyerId != userId)
            {
                // _logger.LogWarning("ConfirmReceipt: user {UserId} is not the buyer of sale #{SaleId}.", userId, id);

                return Forbid();
            }

            // can only confirm receipt on a Paid or Dispatched sale.
            // once settled the status flips to Completed, so a second call is rejected here.
            if (sale.Status != "Paid" && sale.Status != "Dispatched")
            {
                // _logger.LogWarning("ConfirmReceipt: sale #{SaleId} has status {Status}; expected Paid or Dispatched.", id, sale.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "The sale is not awaiting receipt confirmation."
                });
            }

            var delivery = await _context.DeliveryRecords
                .FirstOrDefaultAsync(d => d.SaleId == sale.Id);

            if (delivery == null || delivery.Status != "Dispatched")
            {
                // _logger.LogWarning("ConfirmReceipt: delivery for sale #{SaleId} not in Dispatched state.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "The product must be dispatched before receipt can be confirmed."
                });
            }

            // escrow has to be there and match the sale amount
            var escrow = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.SaleId == sale.Id &&
                    r.Type == "SaleEscrow" &&
                    r.Status == "Reserved");

            if (escrow == null || escrow.Amount != sale.FinalAmount)
            {
                // _logger.LogError("ConfirmReceipt: escrow missing or amount mismatch for sale #{SaleId} (expected {Amount}).",
                //     id, sale.FinalAmount);

                return BadRequest(new
                {
                    status = 400,
                    message = "The sale escrow could not be verified."
                });
            }

            var commission = sale.CommissionAmount;
            var sellerProceeds = sale.SellerProceeds;

            // sanity check on the split
            if (commission < 0 ||
                sellerProceeds < 0 ||
                commission + sellerProceeds != sale.FinalAmount)
            {
                // _logger.LogError("ConfirmReceipt: commission/proceeds split invalid on sale #{SaleId} (commission={Commission}, proceeds={Proceeds}, total={Total}).",
                //     id, commission, sellerProceeds, sale.FinalAmount);

                return BadRequest(new
                {
                    status = 400,
                    message = "The sale amounts do not match."
                });
            }

            var sellerWallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == sale.SellerId);

            if (sellerWallet == null)
            {
                // _logger.LogError("ConfirmReceipt: seller wallet for user {SellerId} not found.", sale.SellerId);

                return BadRequest(new
                {
                    status = 400,
                    message = "The seller's wallet was not found."
                });
            }

            var platformUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName == "platform-system-account");

            if (platformUser == null)
            {
                // _logger.LogError("ConfirmReceipt: platform system account not found.");

                return BadRequest(new
                {
                    status = 400,
                    message = "The platform system account was not found."
                });
            }

            var platformWallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == platformUser.Id);

            if (platformWallet == null)
            {
                // _logger.LogError("ConfirmReceipt: platform wallet for user {PlatformUserId} not found.", platformUser.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "The platform wallet was not found."
                });
            }

            if (platformWallet.Id == sellerWallet.Id)
            {
                // _logger.LogError("ConfirmReceipt: platform wallet and seller wallet resolve to the same wallet #{WalletId}.", sellerWallet.Id);

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

            // release the seller's deposit (if one exists)
            var sellerDeposit = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == sale.ProductId &&
                    r.WalletId == sellerWallet.Id &&
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved");

            if (sellerDeposit != null)
            {
                // if the reserved balance is somehow out of sync, clamp to 0
                // (can happen if an admin manually adjusted balances)
                if (sellerWallet.ReservedBalance < sellerDeposit.Amount)
                {
                    sellerWallet.ReservedBalance = 0;
                }
                else
                {
                    // un-reserve — money stays in balance, just spendable again
                    sellerWallet.ReservedBalance -= sellerDeposit.Amount;
                }

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

            // release every buyer's eligibility reservation for this auction
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
                    // _logger.LogError("ConfirmReceipt: buyer reservation #{ReservationId} could not be safely released (wallet #{WalletId}).",
                    //     reservation.Id, reservation.WalletId);

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

            // wrap it up
            escrow.Status = "Released";
            delivery.Status = "ReceiptConfirmed";
            delivery.ReceiptConfirmedAt = now;
            delivery.ReceiptProofUrl = receiptImageUrl;
            delivery.ReceiptNotes = string.IsNullOrEmpty(dto.ReceiptNotes) ? null : dto.ReceiptNotes.Trim();
            sale.Status = "Completed";

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();


            // System.Diagnostics.Debug.Assert(sale.Status == "Completed");
            // System.Diagnostics.Debug.Assert(delivery.Status == "ReceiptConfirmed");
            // System.Diagnostics.Debug.Assert(escrow.Status == "Released");
            // System.Diagnostics.Debug.Assert(sellerWallet.Balance >= sellerProceeds);

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