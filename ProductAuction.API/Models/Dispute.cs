
namespace ProductAuction.API.Models
{
    public class Dispute
    {
        public int Id { get; set; }

        public int SaleId { get; set; }
        public int ProductId { get; set; }

        public string RaisedBy { get; set; } = string.Empty;

        public string Complaint { get; set; } = string.Empty;
        public string? EvidenceUrl { get; set; }

        // Expected values: Open, UnderReview, Resolved, Rejected
        public string Status { get; set; } = "Open";

        public string? AdminDecision { get; set; }
        public string? DecidedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
