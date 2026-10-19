using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Dispute
{
    public class ResolveDisputeDto
    {
        [Required]
        public string FaultType { get; set; } = string.Empty;

        [Required]
        [StringLength(2000, MinimumLength = 10)]
        public string AdminDecision { get; set; } = string.Empty;

        [Required]
        public bool ApplyPenalty { get; set; }
    }
}