
namespace ProductAuction.API.DTOs.Profile
{
    public class ProfileVerificationResponseDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? NationalIdNumber { get; set; }
        public string? NationalIdFrontImageUrl { get; set; }
        public string? NationalIdBackImageUrl { get; set; }
        public string VerificationStatus { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
