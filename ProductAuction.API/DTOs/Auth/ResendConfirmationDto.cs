using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Auth
{
    public class ResendConfirmationDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
