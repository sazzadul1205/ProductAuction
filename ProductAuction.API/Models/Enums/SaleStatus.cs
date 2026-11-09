namespace ProductAuction.API.Models.Enums
{
    public enum SaleStatus
    {
        AwaitingPayment,
        Paid,
        PaymentOverdue,
        Dispatched,
        Completed,
        Disputed,
        Refunded
    }
}