using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly AppDbContext _context;

        public NotificationController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/notification
        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] PaginationQueryDto query)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

            var notificationsQuery = _context.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId);

            var filter = !string.IsNullOrWhiteSpace(query.Filter) ? query.Filter : query.Status;
            if (string.Equals(filter, "Unread", StringComparison.OrdinalIgnoreCase))
            {
                notificationsQuery = notificationsQuery.Where(n => !n.IsRead);
            }

            notificationsQuery = notificationsQuery.OrderByDescending(n => n.CreatedAt);

            var totalCount = await notificationsQuery.CountAsync();
            var unreadCount = await _context.Notifications.AsNoTracking()
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            var items = await notificationsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Message,
                    n.LinkUrl,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Notifications retrieved successfully.",
                data = new
                {
                    items,
                    unreadCount
                },
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount
                }
            });
        }

        // GET: api/notification/unread-count
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            var count = await _context.Notifications
                .AsNoTracking()
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return Ok(new
            {
                status = 200,
                message = "Unread count retrieved successfully.",
                data = new { count }
            });
        }

        // PUT: api/notification/{id}/read
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification == null)
            {
                return NotFound(new { status = 404, message = "Notification not found." });
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                status = 200,
                message = "Notification marked as read.",
                data = new { notification.Id, notification.IsRead }
            });
        }

        // PUT: api/notification/read-all
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { status = 401, message = "Please log in first." });
            }

            var unread = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in unread)
            {
                n.IsRead = true;
            }

            if (unread.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                status = 200,
                message = "All notifications marked as read.",
                data = new { updated = unread.Count }
            });
        }
    }
}
