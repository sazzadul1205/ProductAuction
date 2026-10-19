using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Dispute
{
    public class CreateDisputeDto
    {

        [Required]
        public int SaleId { get; set; }

        [Required]
        [StringLength(2000, MinimumLength = 10)]
        public string Complaint { get; set; } = string.Empty;

        [StringLength(500)]
        public string? EvidenceUrl { get; set; }
    }
}
