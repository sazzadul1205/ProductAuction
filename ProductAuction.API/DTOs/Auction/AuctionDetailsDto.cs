public class AuctionDetailsDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public string ProductDescription { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public decimal BuyoutPrice { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? CurrentHighestBid { get; set; }
    public int BidCount { get; set; }
    public string? Result { get; set; }
    public string? MainImageUrl { get; set; }
    // Only populated by the seller-only view endpoint.
    public string? CancellationReason { get; set; }

    // Enriched product metadata used to render a rich Item Details panel.
    public string? Brand { get; set; }
    public string? ModelNumber { get; set; }
    public string? Condition { get; set; }
    public string? CategoryName { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public string? UsageHistory { get; set; }
    public string? ReasonForSelling { get; set; }
    public string? KnownIssues { get; set; }
}