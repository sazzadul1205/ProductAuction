using Microsoft.AspNetCore.Mvc;
using ProductAuction.API.Data;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<HealthController> _logger;

        public HealthController(AppDbContext context, ILogger<HealthController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/health
        [HttpGet]
        public async Task<IActionResult> GetHealth()
        {
            bool dbOk;

            try
            {
                dbOk = await _context.Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                dbOk = false;
                _logger.LogWarning(ex, "Health check database probe failed.");
            }

            var healthy = dbOk;

            return Ok(new
            {
                status = 200,
                healthy,
                database = dbOk ? "up" : "down",
                serverTime = DateTime.UtcNow,
                message = healthy ? "OK" : "Degraded"
            });
        }
    }
}