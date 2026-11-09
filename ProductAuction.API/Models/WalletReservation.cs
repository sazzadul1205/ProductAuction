using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Models
{
    public class WalletReservation
    {
        public int Id { get; set; }

        public int WalletId { get; set; }

        public int? ProductId { get; set; }
        public int? AuctionId { get; set; }
        public int? SaleId { get; set; }

        public ReservationType Type { get; set; }

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;
    }
}