namespace ProductAuction.API.Models
{
    public class UserProfile
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        public string? ProfilePhotoUrl { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }

        public string? NationalIdNumber { get; set; }
        public string? NationalIdFrontImageUrl { get; set; }
        public string? NationalIdBackImageUrl { get; set; }

        public string VerificationStatus { get; set; } = "NotSubmitted";

        public string? VerifiedBy { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
