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
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                // _logger.LogWarning("GetMyWallet: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // _logger.LogInformation("GetMyWallet called by user {UserId}.", userId);

            // find the wallet linked to this user
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("GetMyWallet: wallet for user {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // only send what the user actually needs
            var response = new WalletResponseDto
            {
                Id = wallet.Id,
                Balance = wallet.Balance,
                ReservedBalance = wallet.ReservedBalance
            };

            return Ok(new
            {
                status = 200,
                message = "Wallet retrieved successfully.",
                data = response
            });
        }


        // POST: api/wallet/deposit-request
        // Creates a deposit request for the logged-in user's wallet.
        [HttpPost("deposit-request")]
        public async Task<IActionResult> CreateDepositRequest([FromForm] CreateDepositRequestDto dto)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                // _logger.LogWarning("CreateDepositRequest: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // _logger.LogInformation("CreateDepositRequest called by user {UserId} (method='{Method}', amount={Amount}).",
            //     userId, dto.Method, dto.Amount);

            // find the user's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("CreateDepositRequest: wallet for user {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // upload the receipt image
            var receiptImageUrl = await _imageUploadService.UploadImageAsync(dto.ReceiptImage);

            // save the deposit request as pending verification
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


            // System.Diagnostics.Debug.Assert(depositRequest.Id > 0);
            // System.Diagnostics.Debug.Assert(depositRequest.Status == "PendingVerification");

            // _logger.LogInformation("CreateDepositRequest: deposit request #{DepositRequestId} created for wallet #{WalletId}.",
            //     depositRequest.Id, wallet.Id);

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
                // _logger.LogWarning("GetMyDepositRequests: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("GetMyDepositRequests: wallet for user {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Normalize the page number — never below 1
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size — default 20, hard cap at 100
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Free-text search term (may be null or empty)
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

            // apply search filter — matches method or transaction reference
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


            // System.Diagnostics.Debug.Assert(requests.Count <= pageSize);

            // _logger.LogInformation("GetMyDepositRequests returned {Count} of {Total} request(s) for user {UserId}.",
            //     requests.Count, totalCount, userId);

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
                // _logger.LogWarning("GetMyTransactions: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("GetMyTransactions: wallet for user {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
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

            // apply search filter — matches reference or transaction type
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


            // System.Diagnostics.Debug.Assert(transactions.Count <= pageSize);

            // _logger.LogInformation("GetMyTransactions returned {Count} of {Total} transaction(s) for user {UserId}.",
            //     transactions.Count, totalCount, userId);

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
                // _logger.LogWarning("GetMyReservations: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("GetMyReservations: wallet for user {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Normalize the page number — never below 1
            var page = query.Page;
            if (page < 1) page = 1;

            // Normalize the page size — default 20, hard cap at 100
            var pageSize = query.PageSize;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Free-text search term (may be null or empty)
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

            // apply search filter — matches reservation type or product title
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
                .OrderBy(r => r.Id)
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


            // System.Diagnostics.Debug.Assert(reservations.Count <= pageSize);

            // _logger.LogInformation("GetMyReservations returned {Count} of {Total} reservation(s) for user {UserId}.",
            //     reservations.Count, totalCount, userId);

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