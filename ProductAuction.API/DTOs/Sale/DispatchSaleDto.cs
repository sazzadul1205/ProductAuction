using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Sale
{
    public class DispatchSaleDto
    {
        [Required]
        public string DispatchInfo { get; set; } = string.Empty;

        [Required]
        public string TrackingEvidence { get; set; } = string.Empty;
    }
}
