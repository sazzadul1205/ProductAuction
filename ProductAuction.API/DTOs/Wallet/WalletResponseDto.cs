namespace ProductAuction.API.DTOs.Wallet
{
    public class WalletResponseDto
    {
        public int Id { get; set; }
        public decimal Balance { get; set; }
        public decimal ReservedBalance { get; set; }
    }
}