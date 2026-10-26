namespace ProductAuction.API.DTOs.Wallet
{
    public class DepositRequestResponseDto
    {
        public int Id { get; set; }
        public int WalletId { get; set; }
        public string Method { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string TransactionReference { get; set; } = string.Empty;
        public string ReceiptImageUrl { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}