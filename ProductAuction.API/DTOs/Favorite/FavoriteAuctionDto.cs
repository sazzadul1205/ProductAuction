namespace ProductAuction.API.DTOs.Favorite
{
    public class FavoriteAuctionDto
    {
        public int FavoriteId { get; set; }
        public DateTime FavoritedAt { get; set; }
        public int AuctionId { get; set; }
        public string ProductTitle { get; set; } = string.Empty;
        public string? MainImageUrl { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public decimal StartingPrice { get; set; }
        public decimal BuyoutPrice { get; set; }
        public decimal? CurrentHighestBid { get; set; }
        public int BidCount { get; set; }
        public int ViewCount { get; set; }
        public bool IsFeatured { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Result { get; set; }
    }
}
