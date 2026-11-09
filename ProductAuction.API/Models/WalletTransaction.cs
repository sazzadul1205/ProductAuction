using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Models
{
    public class WalletTransaction
    {
        public int Id { get; set; }
        public int WalletId { get; set; }

        public WalletTransactionType Type { get; set; }

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        [Precision(18, 2)]
        public decimal BalanceAfter { get; set; }

        public string? Reference { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}