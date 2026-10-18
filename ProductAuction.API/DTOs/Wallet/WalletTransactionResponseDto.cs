namespace ProductAuction.API.DTOs.Wallet
{
    public class WalletTransactionResponseDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }
        public string? Reference { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}