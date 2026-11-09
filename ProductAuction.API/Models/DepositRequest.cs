using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Models
{
    public class DepositRequest
    {
        public int Id { get; set; }
        public int WalletId { get; set; }

        public string Method { get; set; } = string.Empty;
        [Precision(18, 2)]
        public decimal Amount { get; set; }
        public string TransactionReference { get; set; } = string.Empty;
        public string ReceiptImageUrl { get; set; } = string.Empty;

        public DepositRequestStatus Status { get; set; } = DepositRequestStatus.PendingVerification;

        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}