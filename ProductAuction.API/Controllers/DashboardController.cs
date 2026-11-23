using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Controllers
{
    [Route("api/dashboard")]
    [ApiController]
    [Authorize(Roles = "Admin,Super Admin")]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _context;
        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("admin/analytics")]
        public async Task<IActionResult> AdminAnalytics([FromQuery] string period = "30days")
        {
            int days = period == "7days" ? 7 : period == "6months" ? 180 : period == "12months" ? 365 : 30;
            var now = DateTime.UtcNow.Date;

            // Calculate the start date for the given number of days, (including today)
            var start = now.AddDays(-days + 1);

            var grouped = await _context.UserProfiles
                .Where(p => p.CreatedAt >= start)
                .GroupBy(p => p.CreatedAt.Date)
                .Select(g => new { date = g.Key, count = g.Count() })
                .ToListAsync();

            var userGrowth = new List<object>();

            for (var d = start; d <= now; d = d.AddDays(1))
            {
                var c = grouped.FirstOrDefault(x => x.date == d)?.count ?? 0;

                userGrowth.Add(new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    count = c
                });
            }

            var statuses = await _context.Auctions
                .GroupBy(a => a.Status)
                .Select(g => new { status = g.Key.ToString(), count = g.Count() })
                .ToListAsync();

            var sales = await _context.Sales
                .Where(sa => sa.Status == SaleStatus.Completed)
                .Where(sa => sa.CreatedAt >= start)
                .GroupBy(sa => new { sa.CreatedAt.Year, sa.CreatedAt.Month })
                .Select(g => new { year = g.Key.Year, month = g.Key.Month, volume = g.Sum(x => x.FinalAmount), fees = g.Sum(x => x.CommissionAmount) })
                .ToListAsync();

            var monthly = new List<object>();
            for (var d = new DateTime(start.Year, start.Month, 1); d <= now; d = d.AddMonths(1))
            {
                var m = sales.FirstOrDefault(x => x.year == d.Year && x.month == d.Month);
                monthly.Add(new { month = d.ToString("yyyy-MM"), transactionVolume = m?.volume ?? 0, platformFees = m?.fees ?? 0 });
            }

            return Ok(new
            {
                status = 200,
                data = new
                {
                    userGrowth,
                    auctionStatuses = statuses,
                    monthlyFinancialActivity = monthly
                }
            });
        }
    }
}
