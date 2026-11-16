using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Category;
using ProductAuction.API.Models;
using ProductAuction.API.Services;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IImageUploadService _imageUploadService;

        public CategoryController(AppDbContext context, IImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }

        // GET: api/category
        // Returns categories as a nested tree (Parent -> Children).
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.ParentId,
                    c.IconUrl,
                    ProductCount = _context.Products.Count(p => p.CategoryId == c.Id)
                })
                .ToListAsync();

            var lookup = categories.ToDictionary(
                c => c.Id,
                c => new CategoryNodeDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ParentId = c.ParentId,
                    IconUrl = c.IconUrl,
                    ProductCount = c.ProductCount,
                    Children = new List<CategoryNodeDto>()
                });

            var roots = new List<CategoryNodeDto>();
            foreach (var node in lookup.Values.OrderBy(n => n.Name))
            {
                if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            SortChildren(roots);

            return Ok(new
            {
                status = 200,
                message = "Categories retrieved successfully.",
                data = roots
            });
        }

        private static void SortChildren(List<CategoryNodeDto> nodes)
        {
            nodes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var node in nodes)
            {
                SortChildren(node.Children);
            }
        }

        // POST: api/category
        // Only an admin can create a category (supports optional parent and SVG icon).
        [HttpPost]
        [Authorize(Roles = "Admin,Super Admin")]
        [Consumes("multipart/form-data", "application/json")]
        public async Task<IActionResult> CreateCategory([FromForm] CreateCategoryDto dto)
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return BadRequest(new { status = 400, message = "Category name is required." });
            }

            bool exists = await _context.Categories.AnyAsync(c => c.Name == name);
            if (exists)
            {
                return BadRequest(new { status = 400, message = "This category already exists." });
            }

            if (dto.ParentId.HasValue)
            {
                var parentExists = await _context.Categories.AnyAsync(c => c.Id == dto.ParentId.Value);
                if (!parentExists)
                {
                    return BadRequest(new { status = 400, message = "Parent category not found." });
                }
            }

            string? iconUrl = null;
            if (dto.Icon != null && dto.Icon.Length > 0)
            {
                iconUrl = await _imageUploadService.UploadImageAsync(dto.Icon);
            }

            var category = new Category
            {
                Name = name,
                ParentId = dto.ParentId,
                IconUrl = iconUrl
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Category created successfully.",
                data = new
                {
                    category.Id,
                    category.Name,
                    category.ParentId,
                    category.IconUrl,
                    ProductCount = 0,
                    Children = Array.Empty<object>()
                }
            });
        }

        // DELETE: api/category/{id}
        // Only an admin can delete a category.
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return NotFound(new { status = 404, message = "Category not found." });
            }

            var hasChildren = await _context.Categories.AnyAsync(c => c.ParentId == id);
            if (hasChildren)
            {
                return BadRequest(new { status = 400, message = "Cannot delete a category that has subcategories." });
            }

            var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
            if (hasProducts)
            {
                return BadRequest(new { status = 400, message = "Cannot delete a category that is used by products." });
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Ok(new { status = 200, message = "Category deleted successfully." });
        }

        private class CategoryNodeDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int? ParentId { get; set; }
            public string? IconUrl { get; set; }
            public int ProductCount { get; set; }
            public List<CategoryNodeDto> Children { get; set; } = new();
        }
    }
}