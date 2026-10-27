using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Sale
{
    public class ConfirmReceiptDto
    {
        [Required]
        public IFormFile ReceiptProofImage { get; set; } = null!;

        [MaxLength(1000)]
        public string? ReceiptNotes { get; set; }
    }
}