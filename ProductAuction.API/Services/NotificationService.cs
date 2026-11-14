using ProductAuction.API.Data;
using ProductAuction.API.Models;

namespace ProductAuction.API.Services
{
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _context;

        public NotificationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task CreateNotificationAsync(string userId, string title, string message, string? linkUrl = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var notification = new Notification
            {
                UserId = userId,
                Title = title ?? string.Empty,
                Message = message ?? string.Empty,
                LinkUrl = linkUrl,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }
    }
}
