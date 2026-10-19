
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class Auction
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        public Product Product { get; set; } = null!;
        public string SellerId { get; set; } = string.Empty;

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        // Expected values: Upcoming, Active, Completed, Cancelled
        public string Status { get; set; } = "Upcoming";
        public string? CancellationReason { get; set; }

        [Precision(18, 2)]
        public decimal? CurrentHighestBid { get; set; }
        public string? CurrentHighestBidderId { get; set; }

        public int BidCount { get; set; } = 0;

        public string? WinnerId { get; set; }

        // Expected values: Sold, NoWinner
        public string? Result { get; set; }

        // Used by EF Core to detect conflicting updates.
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
