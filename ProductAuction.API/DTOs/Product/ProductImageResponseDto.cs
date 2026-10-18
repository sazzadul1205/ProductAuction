
namespace ProductAuction.API.DTOs.Product
{
    public class ProductImageResponseDto
    {
        public int Id { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public bool IsMain { get; set; }
    }
}
