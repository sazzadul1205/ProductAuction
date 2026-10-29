using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Auction
{
    public class UpdateAuctionScheduleDto
    {
        [Required]
        public DateTime? StartTime { get; set; }

        [Required]
        public DateTime? EndTime { get; set; }
    }
}