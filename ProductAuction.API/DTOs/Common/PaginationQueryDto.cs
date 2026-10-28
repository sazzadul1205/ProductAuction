namespace ProductAuction.API.DTOs.Common
{
    public class PaginationQueryDto
    {
        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? Search { get; set; }

        public string? Filter { get; set; }

        public int? CategoryId { get; set; }
    }
}