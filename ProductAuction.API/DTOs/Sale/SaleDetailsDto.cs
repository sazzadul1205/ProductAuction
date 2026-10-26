namespace ProductAuction.API.DTOs.Sale
{
    public class SaleDetailsDto
    {
        // Sale information
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;

        public decimal FinalAmount { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal SellerProceeds { get; set; }

        public DateTime PaymentDeadline { get; set; }
        public DateTime? PaymentSubmittedAt { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Buyer and seller
        public string BuyerId { get; set; } = string.Empty;
        public string SellerId { get; set; } = string.Empty;

        // Product information
        public int ProductId { get; set; }
        public string ProductTitle { get; set; } = string.Empty;
        public string ProductDescription { get; set; } = string.Empty;
        public string? MainImageUrl { get; set; }
        public decimal StartingPrice { get; set; }
        public decimal BuyoutPrice { get; set; }

        // Auction result
        public int AuctionId { get; set; }
        public string AuctionStatus { get; set; } = string.Empty;
        public string? WinnerId { get; set; }
        public int BidCount { get; set; }
        public decimal? WinningBidAmount { get; set; }
        public DateTime? WinningBidTime { get; set; }

        // Delivery information
        public string? DeliveryStatus { get; set; }
        public DateTime? DispatchDeadline { get; set; }
        public string? DispatchInfo { get; set; }
        public string? TrackingEvidence { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public DateTime? ReceiptConfirmedAt { get; set; }

        // Proof uploaded by the buyer when confirming receipt.
        // Lets the seller verify the item was actually received.
        public string? ReceiptProofUrl { get; set; }
        public string? ReceiptNotes { get; set; }
    }
}
