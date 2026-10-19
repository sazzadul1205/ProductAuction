
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class Bid
    {
        public int Id { get; set; }
        public int AuctionId { get; set; }

        public Auction Auction { get; set; } = null!;

        public string BidderId { get; set; } = string.Empty;
        [Precision(18, 2)]
        public decimal Amount { get; set; }
        public bool IsBuyout { get; set; } = false;
        public DateTime PlacedAt { get; set; }
    }
}
