using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Dispute
{
    public class CreateDisputeMessageDto
    {
        [Required]
        [StringLength(2000, MinimumLength = 1)]
        public string Body { get; set; } = string.Empty;

        public IFormFile? Attachment { get; set; }
    }
}