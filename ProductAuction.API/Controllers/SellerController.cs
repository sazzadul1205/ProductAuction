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

    public SellerController(AppDbContext context)
    {
      _context = context;
    }

    // GET: api/Seller/dashboard
    // Retrieves aggregated statistics for the seller dashboard,
    // including product counts, active auctions, sales earnings, and wallet balances.
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
      // Get the logged-in seller's ID from the JWT.
      var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (string.IsNullOrEmpty(userId)) return Unauthorized();

      // 1. Product Statistics
      // Retrieve all products listed by this seller.
      var products = await _context.Products
          .Where(p => p.SellerId == userId)
          .ToListAsync();

      var totalProducts = products.Count;
      var pendingApproval = products.Count(p => p.ApprovalStatus == "PendingApproval");
      var approved = products.Count(p => p.ApprovalStatus == "Approved");
      var rejected = products.Count(p => p.ApprovalStatus == "Rejected");

      // 2. Auction Statistics
      // Count the seller's currently live auctions.
      var activeAuctions = await _context.Auctions
          .CountAsync(a => a.SellerId == userId && a.Status == "Active");

      // 3. Sales & Earnings
      // Retrieve all sales associated with this seller.
      var sales = await _context.Sales
          .Where(s => s.SellerId == userId)
          .ToListAsync();

      var soldCount = sales.Count(s => s.Status == "Completed");
      var awaitingDispatch = sales.Count(s => s.Status == "Paid");

      // Calculate total revenue from successfully completed sales.
      var totalEarnings = sales
          .Where(s => s.Status == "Completed")
          .Sum(s => s.SellerProceeds);

      // 4. Wallet Statistics
      // Find the seller's wallet to calculate their available spendable balance.
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