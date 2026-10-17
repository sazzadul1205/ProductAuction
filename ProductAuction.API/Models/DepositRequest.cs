
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class DepositRequest
    {
        public int Id { get; set; }
        public int WalletId { get; set; }

        // Expected values: BankTransfer, bKash
        public string Method { get; set; } = string.Empty;
        [Precision(18, 2)]
        public decimal Amount { get; set; }
        public string TransactionReference { get; set; } = string.Empty;
        public string ReceiptImageUrl { get; set; } = string.Empty;

        // Expected values: PendingVerification, Approved, Rejected
        public string Status { get; set; } = "PendingVerification";

        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
