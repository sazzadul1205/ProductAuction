using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Category;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.Models;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CategoryController(AppDbContext context)
        {
            _context = context;
        }


        // GET: api/category
        // Anyone can view the available categories.
        [HttpGet]
        public async Task<IActionResult> GetCategories([FromQuery] PaginationQueryDto query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);
            var search = query.Search;

            var categoriesQuery = _context.Categories.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                categoriesQuery = categoriesQuery.Where(c => c.Name.ToLower().Contains(term));
            }

            var totalCount = await categoriesQuery.CountAsync();

            var categories = await categoriesQuery
                .OrderBy(c => c.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    ProductCount = _context.Products.Count(p => p.CategoryId == c.Id)
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Categories retrieved successfully.",
                data = categories,
                page,
                pageSize,
                totalCount
            });
        }


        // POST: api/category
        // Only an admin can create a category.
        [HttpPost]
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> CreateCategory(CreateCategoryDto dto)
        {
            var name = dto.Name;

            if (string.IsNullOrEmpty(name))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Category name is required."
                });
            }

            bool exists = await _context.Categories.AnyAsync(c => c.Name == name);
            if (exists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This category already exists."
                });
            }

            var category = new Category
            {
                Name = name
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 201,
                message = "Category created successfully.",
                data = new { category.Id, category.Name, ProductCount = 0 }
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
                return NotFound(new
                {
                    status = 404,
                    message = "Category not found."
                });
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Category deleted successfully."
            });
        }
    }
}