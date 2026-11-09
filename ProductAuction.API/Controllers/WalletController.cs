using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Wallet;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;
using ProductAuction.API.Services;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IImageUploadService _imageUploadService;
        private readonly ILogger<WalletController> _logger;

        public WalletController(
            AppDbContext context,
            IImageUploadService imageUploadService,
            ILogger<WalletController> logger)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _logger = logger;
        }


        // GET: api/wallet/me
        // Returns the wallet belonging to the logged-in user.
        [HttpGet("me")]
        public async Task<IActionResult> GetMyWallet()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // find the wallet linked to this user
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            var availableBalance = wallet.Balance - wallet.ReservedBalance;

            return Ok(new
            {
                status = 200,
                message = "Wallet retrieved successfully.",
                data = new
                {
                    id = wallet.Id,
                    balance = wallet.Balance,
                    reservedBalance = wallet.ReservedBalance,
                    availableBalance
                }
            });
        }


        // POST: api/wallet/deposit-request
        // Creates a deposit request for the logged-in user's wallet.
        [HttpPost("deposit-request")]
        public async Task<IActionResult> CreateDepositRequest([FromForm] CreateDepositRequestDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }
            if (dto == null || dto.ReceiptImage == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Deposit request data and receipt image are required."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            var receiptImageUrl = await _imageUploadService.UploadImageAsync(dto.ReceiptImage);

            var depositRequest = new DepositRequest
            {
                WalletId = wallet.Id,
                Method = dto.Method,
                Amount = dto.Amount,
                TransactionReference = dto.TransactionReference,
                ReceiptImageUrl = receiptImageUrl,
                Status = DepositRequestStatus.PendingVerification,
                CreatedAt = DateTime.UtcNow
            };

            _context.DepositRequests.Add(depositRequest);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Deposit request submitted successfully.",
                data = new
                {
                    depositRequest.Id,
                    depositRequest.Amount,
                    depositRequest.Method,
                    depositRequest.Status,
                    depositRequest.CreatedAt
                }
            });
        }


        // GET: api/wallet/deposit-requests
        // Returns the deposit requests submitted by the logged-in user.
        [HttpGet("deposit-requests")]
        public async Task<IActionResult> GetMyDepositRequests([FromQuery] PaginationQueryDto query)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);
            var search = query.Search;
            var filter = query.Filter;

            var requestsQuery = _context.DepositRequests
                .Where(d => d.WalletId == wallet.Id)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter) && filter != "All"
                && Enum.TryParse<DepositRequestStatus>(filter, ignoreCase: true, out var parsedStatus))
            {
                requestsQuery = requestsQuery.Where(d => d.Status == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                requestsQuery = requestsQuery.Where(d =>
                    d.Method.Contains(term) ||
                    d.TransactionReference.Contains(term));
            }

            requestsQuery = requestsQuery.OrderByDescending(d => d.CreatedAt);

            var totalCount = await requestsQuery.CountAsync();

            var requests = await requestsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new DepositRequestResponseDto
                {
                    Id = d.Id,
                    WalletId = d.WalletId,
                    Method = d.Method,
                    Amount = d.Amount,
                    TransactionReference = d.TransactionReference,
                    ReceiptImageUrl = d.ReceiptImageUrl,
                    Status = d.Status.ToString(),
                    RejectionReason = d.RejectionReason,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Deposit requests retrieved successfully.",
                data = new
                {
                    items = requests,
                    page,
                    pageSize,
                    totalCount
                }
            });
        }


        // GET: api/wallet/transactions
        // Returns the transaction history for the logged-in user's wallet.
        [HttpGet("transactions")]
        public async Task<IActionResult> GetMyTransactions([FromQuery] PaginationQueryDto query)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);
            var search = query.Search;
            var filter = query.Filter;

            var txQuery = _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter) && filter != "All"
                && Enum.TryParse<WalletTransactionType>(filter, ignoreCase: true, out var parsedType))
            {
                txQuery = txQuery.Where(t => t.Type == parsedType);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                txQuery = txQuery.Where(t =>
                    (t.Reference != null && t.Reference.Contains(term)) ||
                    t.Type.ToString().Contains(term));
            }

            txQuery = txQuery.OrderByDescending(t => t.CreatedAt);

            var totalCount = await txQuery.CountAsync();

            var transactions = await txQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new WalletTransactionResponseDto
                {
                    Id = t.Id,
                    Type = t.Type.ToString(),
                    Amount = t.Amount,
                    BalanceAfter = t.BalanceAfter,
                    Reference = t.Reference,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Wallet transactions retrieved successfully.",
                data = new
                {
                    items = transactions,
                    page,
                    pageSize,
                    totalCount
                }
            });
        }


        // GET: api/wallet/reservations
        // Returns the logged-in user's reservation list.
        [HttpGet("reservations")]
        public async Task<IActionResult> GetMyReservations([FromQuery] PaginationQueryDto query)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);
            var search = query.Search;
            var filter = query.Filter;

            var reservationsQuery = _context.WalletReservations
                .Where(r => r.WalletId == wallet.Id)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter) && filter != "All"
                && Enum.TryParse<ReservationStatus>(filter, ignoreCase: true, out var parsedStatus))
            {
                reservationsQuery = reservationsQuery.Where(r => r.Status == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                reservationsQuery = reservationsQuery.Where(r =>
                    r.Type.ToString().Contains(term) ||
                    (r.ProductId != null && matchingProductIds.Contains(r.ProductId.Value)));
            }

            var totalCount = await reservationsQuery.CountAsync();

            var reservations = await reservationsQuery
                .OrderByDescending(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.ProductId,

                    ProductTitle = _context.Products
                        .Where(p => p.Id == r.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),

                    r.AuctionId,
                    r.SaleId,
                    Type = r.Type.ToString(),
                    r.Amount,
                    Status = r.Status.ToString()
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Wallet reservations retrieved successfully.",
                data = new
                {
                    items = reservations,
                    page,
                    pageSize,
                    totalCount
                }
            });
        }
    }
}