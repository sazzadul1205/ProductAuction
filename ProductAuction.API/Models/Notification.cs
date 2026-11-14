using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty; // IdentityUserId
        [MaxLength(150)]
        public string Title { get; set; } = string.Empty;
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;
        public string? LinkUrl { get; set; } // e.g., "/buyer/won", "/admin/disputes/5"
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
