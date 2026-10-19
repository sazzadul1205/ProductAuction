using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Bid
{
    public class BuyoutDto
    {
        [Required]
        public int AuctionId { get; set; }
    }
}
