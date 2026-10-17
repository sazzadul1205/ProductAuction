
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class Product
    {
        public int Id { get; set; }

        public string SellerId { get; set; } = string.Empty;
        public int CategoryId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string ShortSummary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string? Brand { get; set; }
        public string? ModelNumber { get; set; }

        // Expected values: New, LikeNew, Good, Fair, ForPartsRepair
        public string Condition { get; set; } = string.Empty;
        public DateTime? PurchaseDate { get; set; }
        public string UsageHistory { get; set; } = string.Empty;
        public string ReasonForSelling { get; set; } = string.Empty;
        public string KnownIssues { get; set; } = string.Empty;

        [Precision(18, 2)]
        public decimal StartingPrice { get; set; }
        [Precision(18, 2)]
        public decimal BuyoutPrice { get; set; }

        // Expected values: PendingApproval, Approved, Rejected
        public string ApprovalStatus { get; set; } = "PendingApproval";
        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
