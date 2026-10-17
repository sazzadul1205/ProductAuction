
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class Sale
    {
        public int Id { get; set; }

        public int AuctionId { get; set; }
        public int ProductId { get; set; }

        public string BuyerId { get; set; } = string.Empty;
        public string SellerId { get; set; } = string.Empty;

        [Precision(18, 2)]
        public decimal FinalAmount { get; set; }

        [Precision(18, 2)]
        public decimal CommissionAmount { get; set; }

        [Precision(18, 2)]
        public decimal SellerProceeds { get; set; }

        // Expected values: AwaitingPayment, PaymentSubmitted, Paid,
        // PaymentOverdue, Completed, Disputed
        public string Status { get; set; } = "AwaitingPayment";

        public DateTime PaymentDeadline { get; set; }
        public DateTime? PaymentSubmittedAt { get; set; }
        public DateTime? PaymentConfirmedAt { get; set; }

        public string? PaymentConfirmedBy { get; set; }
        public string? PaymentReference { get; set; }
        public DateTime? SellerNotifiedAt { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
