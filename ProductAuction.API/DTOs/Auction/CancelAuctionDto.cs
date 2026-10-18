using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs
{
    public class CancelAuctionDto
    {
        [Required]
        public string Reason { get; set; } = string.Empty;
    }
}