using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Seller")]
    public class SellerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SellerController> _logger;

        public SellerController(AppDbContext context, ILogger<SellerController> logger)
        {
            _context = context;
            _logger = logger;
        }


        // GET: api/Seller/dashboard
        // Returns aggregated stats for the seller dashboard:
        // products, active auctions, sales, earnings, and wallet balance.
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {

                return Unauthorized();
            }

            var products = await _context.Products
                .Where(p => p.SellerId == userId)
                .ToListAsync();

            var totalProducts = products.Count;
            var pendingApproval = products.Count(p => p.ApprovalStatus == "PendingApproval");
            var approved = products.Count(p => p.ApprovalStatus == "Approved");
            var rejected = products.Count(p => p.ApprovalStatus == "Rejected");

            // auction stats
            var activeAuctions = await _context.Auctions
                .CountAsync(a => a.SellerId == userId && a.Status == "Active");

            // sales & earnings
            var sales = await _context.Sales
                .Where(s => s.SellerId == userId)
                .ToListAsync();

            var soldCount = sales.Count(s => s.Status == "Completed");
            var awaitingDispatch = sales.Count(s => s.Status == "Paid");
            var totalEarnings = sales
                .Where(s => s.Status == "Completed")
                .Sum(s => s.SellerProceeds);

            // wallet stats
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            var availableBalance = wallet != null ? (wallet.Balance - wallet.ReservedBalance) : 0;

            return Ok(new
            {
                status = 200,
                data = new
                {
                    totalProducts,
                    pendingApproval,
                    approved,
                    rejected,
                    activeAuctions,
                    soldCount,
                    awaitingDispatch,
                    totalEarnings,
                    availableBalance
                }
            });
        }
    }
}