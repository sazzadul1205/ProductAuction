public class AuctionListDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public decimal BuyoutPrice { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? CurrentHighestBid { get; set; }
    public int BidCount { get; set; }
    public string? MainImageUrl { get; set; }
}