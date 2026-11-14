using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
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
        private readonly INotificationService _notificationService;

        public DisputeController(
            AppDbContext context,
            IImageUploadService imageUploadService,
            ILogger<DisputeController> logger,
            INotificationService notificationService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _logger = logger;
            _notificationService = notificationService;
        }


        // GET: api/dispute
        // Admin views all disputes.
        [HttpGet]
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> GetAllDisputes([FromQuery] PaginationQueryDto query)
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
            var openCount = await _context.Disputes.AsNoTracking().CountAsync(d => d.Status == DisputeStatus.Open);
            var underReviewCount = await _context.Disputes.AsNoTracking().CountAsync(d => d.Status == DisputeStatus.UnderReview);
            var resolvedCount = await _context.Disputes.AsNoTracking().CountAsync(d => d.Status == DisputeStatus.Resolved || d.Status == DisputeStatus.Rejected);

            // Get paginated disputes.
            var disputes = await disputesQuery
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get related sale information.
            var saleIds = disputes.Select(d => d.SaleId).Distinct().ToList();

            var sales = await _context.Sales
                .AsNoTracking()
                .Where(s => saleIds.Contains(s.Id))
                .ToListAsync();

            // Collect everyone involved: the user who raised it + the other party.
            var allUserIds = disputes.Select(d => d.RaisedBy).ToList();

            foreach (var dispute in disputes)
            {
                var sale = sales.FirstOrDefault(s => s.Id == dispute.SaleId);
                if (sale != null)
                {
                    var againstUserId = sale.BuyerId == dispute.RaisedBy ? sale.SellerId : sale.BuyerId;
                    if (!string.IsNullOrEmpty(againstUserId))
                    {
                        allUserIds.Add(againstUserId);
                    }
                }
            }

            allUserIds = allUserIds.Distinct().ToList();

            // Get user profiles.
            var profiles = await _context.UserProfiles
                .AsNoTracking()
                .Where(p => allUserIds.Contains(p.UserId))
                .Select(p => new { p.UserId, p.FullName })
                .ToListAsync();

            // Get user roles (load the links and the roles, then match them up).
            var userRoles = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => allUserIds.Contains(ur.UserId))
                .ToListAsync();

            var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();

            var roles = await _context.Roles
                .AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();

            // Get penalties for these disputes.
            var disputeIds = disputes.Select(d => d.Id).ToList();

            var penalties = await _context.Penalties
                .AsNoTracking()
                .Where(p => disputeIds.Contains(p.DisputeId))
                .ToListAsync();


            string GetRole(string? userId)
            {
                if (string.IsNullOrEmpty(userId)) { return "User"; }
                var userRole = userRoles.FirstOrDefault(ur => ur.UserId == userId);
                if (userRole == null) { return "User"; }
                var role = roles.FirstOrDefault(r => r.Id == userRole.RoleId);
                return role?.Name ?? "User";
            }

            // Prepare final response.
            var result = disputes.Select(d =>
            {
                var sale = sales.FirstOrDefault(s => s.Id == d.SaleId);
                var penalty = penalties.FirstOrDefault(p => p.DisputeId == d.Id);

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

                    RaisedByName = GetRole(d.RaisedBy),
                    RaisedByRole = GetRole(d.RaisedBy),

                    AgainstUserId = againstUserId,
                    AgainstName = GetRole(againstUserId),
                    AgainstRole = GetRole(againstUserId),

                    Penalty = penalty == null
                        ? null
                        : new
                        {
                            penalty.TotalAmount,
                            penalty.OppositePartyAmount,
                            penalty.PlatformAmount,
                            penalty.AgainstUserId
                        }
                };
            }).ToList();

            return Ok(new
            {
                status = 200,
                message = "Disputes retrieved successfully.",
                data = result,
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount
                },
                summary = new
                {
                    openCount,
                    underReviewCount,
                    resolvedCount
                }
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
                return StatusCode(403, new
                {
                    status = 403,
                    message = "Only the buyer or seller involved can dispute this sale."
                });
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


        // GET: api/dispute/{id}
        // Returns dispute details for parties and admins.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDisputeById(int id)
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

            var dispute = await _context.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (dispute == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Dispute not found."
                });
            }

            var sale = await _context.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dispute.SaleId);
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            var isSuperAdmin = User.IsInRole("Super Admin");
            var isAdmin = isSuperAdmin || User.IsInRole("Admin");
            var isParty = sale.BuyerId == userId || sale.SellerId == userId;
            var isAssignedAdmin = isAdmin && (dispute.AssignedAdminId == null || dispute.AssignedAdminId == userId);

            if (!isParty && !isSuperAdmin && !isAssignedAdmin)
            {
                return StatusCode(403, new
                {
                    status = 403,
                    message = "Access denied."
                });
            }

            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dispute.ProductId);
            var penalty = await _context.Penalties.AsNoTracking().FirstOrDefaultAsync(p => p.DisputeId == dispute.Id);

            var involvedUserIds = new List<string> { dispute.RaisedBy, sale.BuyerId, sale.SellerId };
            var profiles = await _context.UserProfiles.AsNoTracking()
                .Where(p => involvedUserIds.Contains(p.UserId))
                .Select(p => new { p.UserId, p.FullName })
                .ToListAsync();

            var userRoles = await _context.UserRoles.AsNoTracking()
                .Where(ur => involvedUserIds.Contains(ur.UserId))
                .ToListAsync();
            var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
            var roles = await _context.Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();

            string GetRole(string? uid)
            {
                if (string.IsNullOrEmpty(uid)) return "User";
                var ur = userRoles.FirstOrDefault(x => x.UserId == uid);
                if (ur == null) return "User";
                return roles.FirstOrDefault(r => r.Id == ur.RoleId)?.Name ?? "User";
            }

            var againstUserId = sale.BuyerId == dispute.RaisedBy ? sale.SellerId : sale.BuyerId;
            var canSendMessages = dispute.Status == DisputeStatus.UnderReview && (isParty || isSuperAdmin || isAssignedAdmin);

            return Ok(new
            {
                status = 200,
                message = "Dispute retrieved successfully.",
                data = new
                {
                    id = dispute.Id,
                    saleId = dispute.SaleId,
                    productId = dispute.ProductId,
                    productTitle = product?.Title,
                    raisedBy = dispute.RaisedBy,
                    raisedByName = GetRole(dispute.RaisedBy),
                    raisedByRole = GetRole(dispute.RaisedBy),
                    againstUserId,
                    againstName = GetRole(againstUserId),
                    againstRole = GetRole(againstUserId),
                    complaint = dispute.Complaint,
                    evidenceUrl = dispute.EvidenceUrl,
                    status = dispute.Status.ToString(),
                    adminDecision = dispute.AdminDecision,
                    decidedBy = dispute.DecidedBy,
                    createdAt = dispute.CreatedAt,
                    canSendMessages,
                    penalty = penalty == null ? null : new
                    {
                        totalAmount = penalty.TotalAmount,
                        oppositePartyAmount = penalty.OppositePartyAmount,
                        platformAmount = penalty.PlatformAmount,
                        againstUserId = penalty.AgainstUserId
                    }
                }
            });
        }

        // GET: api/dispute/{id}/messages
        [HttpGet("{id}/messages")]
        public async Task<IActionResult> GetDisputeMessages(int id)
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

            var dispute = await _context.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (dispute == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Dispute not found."
                });
            }

            var sale = await _context.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dispute.SaleId);
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            var isSuperAdmin = User.IsInRole("Super Admin");
            var isAdmin = isSuperAdmin || User.IsInRole("Admin");
            var isParty = sale.BuyerId == userId || sale.SellerId == userId;
            var isAssignedAdmin = isAdmin && (dispute.AssignedAdminId == null || dispute.AssignedAdminId == userId);
            if (!isParty && !isSuperAdmin && !isAssignedAdmin)
            {
                return StatusCode(403, new
                {
                    status = 403,
                    message = "Access denied."
                });
            }

            var messages = await _context.DisputeMessages.AsNoTracking()
                .Where(m => m.DisputeId == id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            var senderIds = messages.Where(m => !string.IsNullOrEmpty(m.SenderId)).Select(m => m.SenderId).Distinct().ToList();

            var data = messages.Select(m => new
            {
                id = m.Id,
                disputeId = m.DisputeId,
                senderId = m.SenderId,
                senderName = m.SenderRole,
                senderRole = m.SenderRole,
                body = m.Body,
                attachmentUrl = m.AttachmentUrl,
                createdAt = m.CreatedAt
            }).ToList();

            return Ok(new { status = 200, message = "Messages retrieved successfully.", data });
        }

        // POST: api/dispute/{id}/messages
        [HttpPost("{id}/messages")]
        public async Task<IActionResult> SendDisputeMessage(int id, [FromForm] CreateDisputeMessageDto dto)
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

            var dispute = await _context.Disputes.FirstOrDefaultAsync(d => d.Id == id);
            if (dispute == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Dispute not found."
                });
            }

            if (dispute.Status != DisputeStatus.UnderReview)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Messages can only be sent while the dispute is under review."
                });
            }

            var sale = await _context.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dispute.SaleId);
            if (sale == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Related sale not found."
                });
            }

            var isSuperAdmin = User.IsInRole("Super Admin");
            var isAdmin = isSuperAdmin || User.IsInRole("Admin");
            var isBuyer = sale.BuyerId == userId;
            var isSeller = sale.SellerId == userId;
            var isParty = isBuyer || isSeller;

            if (isAdmin && !isSuperAdmin && dispute.AssignedAdminId != null && dispute.AssignedAdminId != userId)
            {
                return StatusCode(403, new
                {
                    status = 403,
                    message = "This dispute is assigned to another admin."
                });
            }
            if (!isParty && !isSuperAdmin && !(isAdmin && (dispute.AssignedAdminId == null || dispute.AssignedAdminId == userId)))
            {
                return StatusCode(403, new
                {
                    status = 403,
                    message = "Access denied."
                });
            }

            // auto-claim unassigned dispute for admin
            if (isAdmin && !isSuperAdmin && dispute.AssignedAdminId == null)
            {
                dispute.AssignedAdminId = userId;
                _context.DisputeMessages.Add(new DisputeMessage
                {
                    DisputeId = dispute.Id,
                    SenderId = userId,
                    SenderRole = "System",
                    Body = "An admin has joined the dispute review.",
                    CreatedAt = DateTime.UtcNow
                });
            }

            string? attachmentUrl = null;
            if (dto.Attachment != null)
            {
                attachmentUrl = await _imageUploadService.UploadImageAsync(dto.Attachment);
            }

            string senderRole = isSuperAdmin ? "Super Admin" : isAdmin ? "Admin" : isSeller ? "Seller" : "Buyer";
            var message = new DisputeMessage
            {
                DisputeId = dispute.Id,
                SenderId = userId,
                SenderRole = senderRole,
                Body = (dto.Body ?? string.Empty).Trim(),
                AttachmentUrl = attachmentUrl,
                CreatedAt = DateTime.UtcNow
            };

            _context.DisputeMessages.Add(message);
            await _context.SaveChangesAsync();

            var disputeLink = $"/admin/disputes";
            var recipients = new HashSet<string>(StringComparer.Ordinal);
            if (isParty)
            {
                var otherPartyId = sale.BuyerId == userId ? sale.SellerId : sale.BuyerId;
                if (!string.IsNullOrEmpty(otherPartyId)) recipients.Add(otherPartyId);
                if (!string.IsNullOrEmpty(dispute.AssignedAdminId) && dispute.AssignedAdminId != userId)
                {
                    recipients.Add(dispute.AssignedAdminId);
                }
            }
            else if (isAdmin || isSuperAdmin)
            {
                if (!string.IsNullOrEmpty(sale.BuyerId) && sale.BuyerId != userId) recipients.Add(sale.BuyerId);
                if (!string.IsNullOrEmpty(sale.SellerId) && sale.SellerId != userId) recipients.Add(sale.SellerId);
            }

            foreach (var recipientId in recipients)
            {
                await _notificationService.CreateNotificationAsync(
                    recipientId,
                    "New dispute message",
                    $"A new message was posted on dispute #{dispute.Id}.",
                    disputeLink);
            }

            return Ok(new
            {
                status = 200,
                message = "Message sent successfully.",
                data = new
                {
                    id = message.Id,
                    disputeId = message.DisputeId,
                    senderId = message.SenderId,
                    senderName = message.SenderRole,
                    senderRole = message.SenderRole,
                    body = message.Body,
                    attachmentUrl = message.AttachmentUrl,
                    createdAt = message.CreatedAt
                }
            });
        }

        // PUT: api/dispute/{id}/resolve
        // Admin decides who's at fault and resolves the dispute.
        [HttpPut("{id}/resolve")]
        [Authorize(Roles = "Admin,Super Admin")]
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

            if (!string.IsNullOrEmpty(sale.BuyerId))
            {
                await _notificationService.CreateNotificationAsync(
                    sale.BuyerId,
                    "Dispute resolved",
                    $"Dispute #{dispute.Id} has been resolved. Decision: {dispute.AdminDecision}",
                    "/buyer/won-auctions");
            }
            if (!string.IsNullOrEmpty(sale.SellerId))
            {
                await _notificationService.CreateNotificationAsync(
                    sale.SellerId,
                    "Dispute resolved",
                    $"Dispute #{dispute.Id} has been resolved. Decision: {dispute.AdminDecision}",
                    "/seller/sales");
            }

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