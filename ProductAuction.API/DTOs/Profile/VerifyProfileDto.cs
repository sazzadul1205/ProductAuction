
using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Profile
{
    public class VerifyProfileDto
    {
        [Required]
        public string Status { get; set; } = string.Empty;

        [StringLength(500)]
        public string? RejectionReason { get; set; }
    }
}
