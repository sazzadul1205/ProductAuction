
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class Penalty
    {
        public int Id { get; set; }

        public int DisputeId { get; set; }
        public string AgainstUserId { get; set; } = string.Empty;

        [Precision(18, 2)]
        public decimal ProductValue { get; set; }

        [Precision(18, 2)]
        public decimal TotalAmount { get; set; }

        [Precision(18, 2)]
        public decimal OppositePartyAmount { get; set; }

        [Precision(18, 2)]
        public decimal PlatformAmount { get; set; }

        // Status audit (non-migration code refs): Proposed=USED, Approved=USED, Applied=USED,
        public string Status { get; set; } = "Proposed";
    }
}
