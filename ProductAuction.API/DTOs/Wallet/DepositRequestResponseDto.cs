using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Wallet
{
    public class DepositRequestResponseDto
    {
        public int Id { get; set; }
        public int WalletId { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string Method { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string TransactionReference { get; set; } = string.Empty;
        public string ReceiptImageUrl { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }
}