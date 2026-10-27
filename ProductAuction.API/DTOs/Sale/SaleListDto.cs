namespace ProductAuction.API.DTOs.Sale
{
    public class SaleListDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductTitle { get; set; } = string.Empty;

        public string BuyerId { get; set; } = string.Empty;
        public string SellerId { get; set; } = string.Empty;
        public string BuyerName { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;

        public decimal FinalAmount { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal SellerProceeds { get; set; }

        public string Status { get; set; } = string.Empty;
        public string DeliveryStatus { get; set; } = string.Empty;

        public DateTime PaymentDeadline { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? MainImageUrl { get; set; }
    }
}