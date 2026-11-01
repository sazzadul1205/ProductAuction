using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Category;
using ProductAuction.API.Models;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CategoryController> _logger;

        public CategoryController(AppDbContext context, ILogger<CategoryController> logger)
        {
            _context = context;
            _logger = logger;
        }


        // GET: api/category
        // Anyone can view the available categories.
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            // _logger.LogInformation("GetCategories called.");

            var categories = await _context.Categories.ToListAsync();

            // _logger.LogInformation("GetCategories returned {Count} category(ies).", categories.Count);


            // System.Diagnostics.Debug.Assert(categories.All(c => c.Id > 0));

            return Ok(new
            {
                status = 200,
                message = "Categories retrieved successfully.",
                data = categories
            });
        }


        // POST: api/category
        // Only an admin can create a category.
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateCategory(CreateCategoryDto dto)
        {
            var name = dto.Name;

            // _logger.LogInformation("CreateCategory called with name '{Name}'.", name);

            // name can't be empty
            if (string.IsNullOrEmpty(name))
            {
                // _logger.LogWarning("CreateCategory: name was empty.");

                return BadRequest(new
                {
                    status = 400,
                    message = "Category name is required."
                });
            }

            // reject duplicates
            bool exists = await _context.Categories.AnyAsync(c => c.Name == name);

            if (exists)
            {
                // _logger.LogWarning("CreateCategory: category '{Name}' already exists.", name);

                return BadRequest(new
                {
                    status = 400,
                    message = "This category already exists."
                });
            }

            // build the category
            var category = new Category
            {
                Name = name
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(category.Id > 0);

            // _logger.LogInformation("CreateCategory: category #{CategoryId} '{Name}' created.", category.Id, category.Name);

            return Ok(new
            {
                status = 200,
                message = "Category created successfully.",
                data = category
            });
        }


        // DELETE: api/category/{id}
        // Only an admin can delete a category.
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            // _logger.LogInformation("DeleteCategory called for category #{CategoryId}.", id);

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                // _logger.LogWarning("DeleteCategory: category #{CategoryId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Category not found."
                });
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            // _logger.LogInformation("DeleteCategory: category #{CategoryId} '{Name}' deleted.", id, category.Name);

            return Ok(new
            {
                status = 200,
                message = "Category deleted successfully."
            });
        }
    }
}