using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
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

        public WalletController(
            AppDbContext context,
            IImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
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
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // find the user's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
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
        public async Task<IActionResult> GetMyDepositRequests([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            // grab the user id from the JWT
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

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // return every deposit request this user submitted, newest first
            var query = _context.DepositRequests
                .Where(d => d.WalletId == wallet.Id)
                .OrderByDescending(d => d.CreatedAt)
                .AsQueryable();

            var totalCount = await query.CountAsync();

            var requests = await query
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
        public async Task<IActionResult> GetMyTransactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // find the user's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // transactions for this wallet, newest first
            var query = _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.CreatedAt)
                .AsQueryable();

            var totalCount = await query.CountAsync();

            var transactions = await query
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
        public async Task<IActionResult> GetMyReservations([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // find the user's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // only reservations that belong to this wallet
            var query = _context.WalletReservations
                .Where(r => r.WalletId == wallet.Id)
                .AsQueryable();

            var totalCount = await query.CountAsync();

            var reservations = await query
                .OrderBy(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.ProductId,

                    // product title (null if the product was deleted)
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