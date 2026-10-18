using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Wallet
{
    public class ReviewDepositRequestDto
    {
        [Required]
        public string Status { get; set; } = string.Empty;

        public string? RejectionReason { get; set; }
    }
}