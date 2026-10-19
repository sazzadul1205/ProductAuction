
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

        public DateTime CreatedAt { get; set; }
    }
}
