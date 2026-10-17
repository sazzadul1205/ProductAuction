
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
    }
}
