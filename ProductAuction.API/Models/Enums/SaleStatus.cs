namespace ProductAuction.API.Models.Enums
{
    public enum SaleStatus
    {
        AwaitingPayment,
        Paid,
        PaymentOverdue,
        Completed,
        Disputed,
        Refunded
    }
}