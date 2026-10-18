
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

        // GET: api/wallet/deposit-requests
        // Returns all deposit requests for admin history.
        [HttpGet("deposit-requests")]
        public async Task<IActionResult> GetAllDepositRequests()
        {
            var requests = await _context.DepositRequests
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DepositRequestResponseDto
                {
                    Id = d.Id,
                    WalletId = d.WalletId,
                    Method = d.Method,
                    Amount = d.Amount,
                    TransactionReference = d.TransactionReference,
                    ReceiptImageUrl = d.ReceiptImageUrl,
                    Status = d.Status,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Deposit request history retrieved successfully.",
                data = requests
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

        // GET: api/wallet/deposit-requests/pending
        // Returns all deposit requests waiting for admin review.
        [HttpGet("deposit-requests/pending")]
        public async Task<IActionResult> GetPendingDepositRequests()
        {
            // Get deposit requests that are still awaiting verification.
            var requests = await _context.DepositRequests
                .Where(d => d.Status == "PendingVerification")
                .OrderBy(d => d.CreatedAt)
                .Select(d => new DepositRequestResponseDto
                {
                    Id = d.Id,
                    WalletId = d.WalletId,
                    Method = d.Method,
                    Amount = d.Amount,
                    TransactionReference = d.TransactionReference,
                    ReceiptImageUrl = d.ReceiptImageUrl,
                    Status = d.Status,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Pending deposit requests retrieved successfully.",
                data = requests
            });
        }

        // PUT: api/wallet/deposit-requests/{id}/review
        // Approves or rejects a deposit request.
        [HttpPut("deposit-requests/{id}/review")]
        public async Task<IActionResult> ReviewDepositRequest(int id, [FromBody] ReviewDepositRequestDto dto)
        {
            // Find the deposit request.
            var depositRequest = await _context.DepositRequests.FirstOrDefaultAsync(d => d.Id == id);

            if (depositRequest == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Deposit request not found."
                });
            }

            // Only Approved or Rejected statuses are allowed.
            if (dto.Status != "Approved" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Approved or Rejected."
                });
            }

            // A rejection must include a reason.
            if (dto.Status == "Rejected" &&
                string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Rejection reason is required."
                });
            }

            // Find the wallet associated with the deposit request.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == depositRequest.WalletId);

            if (wallet == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Wallet not found."
                });
            }

            // Update the review information.
            depositRequest.Status = dto.Status;
            depositRequest.ReviewedAt = DateTime.UtcNow;
            depositRequest.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;

            // Only approved deposits increase the wallet balance.
            if (dto.Status == "Approved")
            {
                wallet.Balance += depositRequest.Amount;

                // Record the wallet balance change.
                var transaction = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = "Credit",
                    Amount = depositRequest.Amount,
                    BalanceAfter = wallet.Balance,
                    Reference = $"DepositRequest-{depositRequest.Id}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.WalletTransactions.Add(transaction);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = dto.Status == "Approved"
                    ? "Deposit request approved successfully."
                    : "Deposit request rejected successfully.",
                data = new
                {
                    depositRequest.Id,
                    depositRequest.Status,
                    depositRequest.RejectionReason,
                    wallet.Balance
                }
            });
        }
    }
}
