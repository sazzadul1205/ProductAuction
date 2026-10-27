namespace ProductAuction.API.DTOs.Bid
{
    public class BidHistoryDto
    {
        public string BidderId { get; set; } = string.Empty;
        
        public string BidderName { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTime PlacedAt { get; set; }
    }
}