using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.Models
{
    public class AuctionView
    {
        public int Id { get; set; }

        public int AuctionId { get; set; }
        public Auction Auction { get; set; } = null!;

        [MaxLength(45)]
        public string IpAddress { get; set; } = string.Empty;

        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }
}
