using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models.Enums;

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

        public AuctionStatus Status { get; set; } = AuctionStatus.Upcoming;
        public string? CancellationReason { get; set; }

        [Precision(18, 2)]
        public decimal? CurrentHighestBid { get; set; }
        public string? CurrentHighestBidderId { get; set; }

        public int BidCount { get; set; } = 0;

        public string? WinnerId { get; set; }

        public AuctionResult? Result { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}