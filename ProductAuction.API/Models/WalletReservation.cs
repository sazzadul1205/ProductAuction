
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class WalletReservation
    {
        public int Id { get; set; }

        public int WalletId { get; set; }

        public int? ProductId { get; set; }
        public int? AuctionId { get; set; }
        public int? SaleId { get; set; }

        // Expected values: SellerDeposit, BuyerFunds, SaleEscrow
        public string Type { get; set; } = string.Empty;

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        // Expected values: Reserved, Released, Forfeited
        public string Status { get; set; } = "Reserved";
    }
}
