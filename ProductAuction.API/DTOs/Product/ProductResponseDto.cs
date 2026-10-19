
namespace ProductAuction.API.DTOs.Product
{
    public class ProductResponseDto
    {
        public int Id { get; set; }

        public string SellerId { get; set; } = string.Empty;

        public int CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? ShortSummary { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Brand { get; set; }

        public string? ModelNumber { get; set; }

        public string Condition { get; set; } = string.Empty;

        public DateTime? PurchaseDate { get; set; }

        public string? UsageHistory { get; set; }

        public string? ReasonForSelling { get; set; }

        public string? KnownIssues { get; set; }

        public decimal StartingPrice { get; set; }

        public decimal BuyoutPrice { get; set; }

        public string ApprovalStatus { get; set; } = string.Empty;

        public string Status { get; set; } = "Available";

        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<ProductImageResponseDto> Images { get; set; } = new();
    }
}
