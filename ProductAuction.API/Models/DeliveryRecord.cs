using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Models
{
    public class DeliveryRecord
    {
        public int Id { get; set; }
        public int SaleId { get; set; }

        public DeliveryStatus Status { get; set; } = DeliveryStatus.AwaitingDispatch;

        public DateTime? DispatchDeadline { get; set; }
        public string? DispatchInfo { get; set; }
        public string? TrackingEvidence { get; set; }

        public DateTime? DispatchedAt { get; set; }
        public DateTime? ReceiptConfirmedAt { get; set; }

        public string? ReceiptProofUrl { get; set; }
        public string? ReceiptNotes { get; set; }
    }
}