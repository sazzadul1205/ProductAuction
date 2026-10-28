namespace ProductAuction.API.DTOs.Common
{
    public class SalesQueryDto : PaginationQueryDto
    {
        public string? DeliveryStatus { get; set; }
    }
}