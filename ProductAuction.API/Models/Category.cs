namespace ProductAuction.API.Models
{
    public class Category
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? ParentId { get; set; }
        public string? IconUrl { get; set; }

        public Category? Parent { get; set; }
        public ICollection<Category> Children { get; set; } = new List<Category>();
    }
}
