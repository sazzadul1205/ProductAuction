
using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Bid
{
    public class PlaceBidDto
    {
        public int AuctionId { get; set; }

        [Required]
        public decimal Amount { get; set; }
    }
}
