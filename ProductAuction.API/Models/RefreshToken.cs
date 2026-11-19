using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.Models
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(500)]
        public string Token { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public bool IsRevoked { get; set; } = false;

        public bool IsExpired { get; set; } = false;

        public bool IsActive { get; set; } = true;
    }
}