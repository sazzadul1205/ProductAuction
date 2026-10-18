
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProductAuction.API.DTOs.Product
{
    public class CreateProductDto
    {
        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(300)]
        public string? ShortSummary { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Brand { get; set; }

        [StringLength(100)]
        public string? ModelNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string Condition { get; set; } = string.Empty;

        public DateTime? PurchaseDate { get; set; }

        public string? UsageHistory { get; set; }

        public string? ReasonForSelling { get; set; }

        public string? KnownIssues { get; set; }

        [Range(typeof(decimal), "0.01", "9999999999")]
        public decimal StartingPrice { get; set; }

        [Range(typeof(decimal), "0.01", "9999999999")]
        public decimal BuyoutPrice { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int CategoryId { get; set; }

        // Product images uploaded with the product form.
        public List<IFormFile>? Images { get; set; }

        public int? MainImageIndex { get; set; }
    }
}
