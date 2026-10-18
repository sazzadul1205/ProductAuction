using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Category
{
    public class CreateCategoryDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
    }
}
