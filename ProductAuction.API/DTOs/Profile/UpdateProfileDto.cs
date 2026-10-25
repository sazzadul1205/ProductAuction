
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProductAuction.API.DTOs.Profile
{
    public class UpdateProfileDto
    {
        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ProfilePhotoUrl { get; set; }

        [StringLength(300)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(20)]
        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? NationalIdNumber { get; set; }

        // Optional NID front image upload.
        public IFormFile? NationalIdFrontImage { get; set; }

        // Optional NID back image upload.
        public IFormFile? NationalIdBackImage { get; set; }
    }
}
