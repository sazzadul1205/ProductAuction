
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class WalletTransaction
    {
        public int Id { get; set; }
        public int WalletId { get; set; }

        // Expected values: Credit, Debit, Reserve, Release, Penalty, Commission
        public string Type { get; set; } = string.Empty;

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        [Precision(18, 2)]
        public decimal BalanceAfter { get; set; }

        public string? Reference { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
