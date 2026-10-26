
namespace ProductAuction.API.Models
{
    public class DeliveryRecord
    {
        public int Id { get; set; }
        public int SaleId { get; set; }

        // Expected values: AwaitingDispatch, Dispatched, Delivered,
        // ReceiptConfirmed, NonDeliveryReported
        public string Status { get; set; } = "AwaitingDispatch";

        public DateTime? DispatchDeadline { get; set; }
        public string? DispatchInfo { get; set; }
        public string? TrackingEvidence { get; set; }

        public DateTime? DispatchedAt { get; set; }
        public DateTime? ReceiptConfirmedAt { get; set; }

        // Proof uploaded by the buyer when they confirm receipt.
        // A photo of the delivered package, a tracking screenshot,
        // or a URL to external evidence. Required before funds are
        // released so the seller has evidence the item was received.
        public string? ReceiptProofUrl { get; set; }
        public string? ReceiptNotes { get; set; }
    }
}
