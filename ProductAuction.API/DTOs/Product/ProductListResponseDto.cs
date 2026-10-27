namespace ProductAuction.API.DTOs.Product
{
    public class ProductListResponseDto
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? ShortSummary { get; set; }

        public decimal StartingPrice { get; set; }

        public decimal BuyoutPrice { get; set; }

        public string ApprovalStatus { get; set; } = string.Empty;

        public string Status { get; set; } = "Available";

        public string? MainImageUrl { get; set; }

        // Used by admin moderation pages to show category/condition/rejection
        public string? CategoryName { get; set; }
        public int CategoryId { get; set; }

        public string? Condition { get; set; }
        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; }

        // Auction info (null when no auction exists for this product).
        // Lets sellers see the live/cancelled/completed status of their auction.
        public string? AuctionStatus { get; set; }
        public DateTime? AuctionStartTime { get; set; }
        public DateTime? AuctionEndTime { get; set; }
        public int? AuctionId { get; set; }
        public int? AuctionBidCount { get; set; }
        public decimal? AuctionCurrentHighestBid { get; set; }
        public string? AuctionCancellationReason { get; set; }
    }
}