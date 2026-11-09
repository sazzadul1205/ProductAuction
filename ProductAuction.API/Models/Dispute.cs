
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

        // Status audit (non-migration code refs): Open=USED, UnderReview=USED, Resolved=USED, Rejected=USED
        public string Status { get; set; } = "Open";

        public string? AdminDecision { get; set; }
        public string? DecidedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
