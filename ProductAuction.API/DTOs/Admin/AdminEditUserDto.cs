using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Admin
{
    public class AdminEditUserDto
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Admin";
    }
}