using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Wallet;
using ProductAuction.API.Models;
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

            var response = new WalletResponseDto
            {
                Id = wallet.Id,
                Balance = wallet.Balance,
                ReservedBalance = wallet.ReservedBalance
            };

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
                Status = "PendingVerification",
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

            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional status filter
            var filter = query.Filter;

            // _logger.LogInformation("GetMyDepositRequests called by user {UserId}: page={Page}, pageSize={PageSize}, filter={Filter}, search={Search}.",
            //     userId, page, pageSize, filter, search);

            var requestsQuery = _context.DepositRequests
                .Where(d => d.WalletId == wallet.Id)
                .AsQueryable();

            // apply status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                requestsQuery = requestsQuery.Where(d => d.Status == filter);
            }

            // apply search filter 
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
                    Status = d.Status,
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

            // Normalize the page number
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional type filter
            var filter = query.Filter;

            // _logger.LogInformation("GetMyTransactions called by user {UserId}: page={Page}, pageSize={PageSize}, filter={Filter}, search={Search}.",
            //     userId, page, pageSize, filter, search);

            var txQuery = _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .AsQueryable();

            // apply type filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                txQuery = txQuery.Where(t => t.Type == filter);
            }

            // apply search filte
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                txQuery = txQuery.Where(t =>
                    (t.Reference != null && t.Reference.Contains(term)) ||
                    t.Type.Contains(term));
            }

            txQuery = txQuery.OrderByDescending(t => t.CreatedAt);

            var totalCount = await txQuery.CountAsync();

            var transactions = await txQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new WalletTransactionResponseDto
                {
                    Id = t.Id,
                    Type = t.Type,
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

            // Normalize the page number Set Search 
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size 
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Set Search 
            var search = query.Search;

            // Optional status filter
            var filter = query.Filter;

            // _logger.LogInformation("GetMyReservations called by user {UserId}: page={Page}, pageSize={PageSize}, filter={Filter}, search={Search}.",
            //     userId, page, pageSize, filter, search);

            var reservationsQuery = _context.WalletReservations
                .Where(r => r.WalletId == wallet.Id)
                .AsQueryable();

            // apply status filter
            if (!string.IsNullOrWhiteSpace(filter) && filter != "All")
            {
                reservationsQuery = reservationsQuery.Where(r => r.Status == filter);
            }

            // apply search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                var matchingProductIds = await _context.Products
                    .Where(p => p.Title.Contains(term))
                    .Select(p => p.Id)
                    .ToListAsync();

                reservationsQuery = reservationsQuery.Where(r =>
                    r.Type.Contains(term) ||
                    (r.ProductId != null && matchingProductIds.Contains(r.ProductId.Value)));

                // _logger.LogDebug("GetMyReservations: search '{Term}' matched {Count} product(s).", term, matchingProductIds.Count);
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
                    r.Type,
                    r.Amount,
                    r.Status
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