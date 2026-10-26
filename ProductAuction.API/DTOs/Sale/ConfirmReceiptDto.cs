using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Sale
{
    public class ConfirmReceiptDto
    {
        [Required]
        [Url]
        [MaxLength(500)]
        public string ReceiptProofUrl { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? ReceiptNotes { get; set; }
    }
}