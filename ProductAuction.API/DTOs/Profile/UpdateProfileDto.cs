
using System.ComponentModel.DataAnnotations;

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

        [StringLength(50)]
        public string? NationalIdNumber { get; set; }

        [StringLength(500)]
        public string? NationalIdFrontImageUrl { get; set; }

        [StringLength(500)]
        public string? NationalIdBackImageUrl { get; set; }
    }
}
