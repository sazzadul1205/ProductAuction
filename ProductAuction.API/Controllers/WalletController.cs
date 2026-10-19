
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

        public WalletController(AppDbContext context,
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
            // Get the user's ID from the JWT token.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // Find the wallet linked to this user.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Return only the wallet information needed by the user.
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
            // Get the logged-in user's ID from the JWT token.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // Find the user's wallet.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Upload the receipt image.
            var receiptImageUrl = await _imageUploadService.UploadImageAsync(dto.ReceiptImage);

            // Save the deposit request as pending verification.
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

        // GET: api/wallet/transactions
        // Returns the transaction history for the logged-in user's wallet.
        [HttpGet("transactions")]
        public async Task<IActionResult> GetMyTransactions()
        {
            // Get the user's ID from the JWT token.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // Find the user's wallet.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Get transactions belonging to this wallet, newest first.
            var transactions = await _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.CreatedAt)
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
                data = transactions
            });
        }

        // GET: api/wallet/reservations
        // Returns the logged-in user's reservation list.
        [HttpGet("reservations")]
        public async Task<IActionResult> GetMyReservations()
        {
            // Get the user's ID from the JWT token.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User ID not found."
                });
            }

            // Find the user's wallet.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Get reservations belonging only to this user's wallet.
            // Get reservations belonging only to this user's wallet.
            var reservations = await _context.WalletReservations
                .Where(r => r.WalletId == wallet.Id)
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
                .OrderBy(r => r.Id)
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Wallet reservations retrieved successfully.",
                data = reservations
            });
        }
   }
}
