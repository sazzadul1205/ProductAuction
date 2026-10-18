
namespace ProductAuction.API.DTOs.Profile
{
    public class ProfileResponseDto
    {
        public string FullName { get; set; } = string.Empty;

        public string? ProfilePhotoUrl { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public string? NationalIdNumber { get; set; }

        public string? NationalIdFrontImageUrl { get; set; }

        public string? NationalIdBackImageUrl { get; set; }

        public string VerificationStatus { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
