using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProductAuction.API.DTOs.Category
{
    public class CreateCategoryDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public IFormFile? Icon { get; set; }
    }
}
