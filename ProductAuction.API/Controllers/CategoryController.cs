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

        public CategoryController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/category
        // Anyone can view the available categories.
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = _context.Categories.ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Categories retrieved successfully.",
                data = categories
            });
        }

        // POST: api/category
        // Only an Admin can create a category.
        [HttpPost]
        public async Task<IActionResult> CreateCategory(CreateCategoryDto dto)
        {

            var Name = dto.Name;

            // Do not accept an empty name.
            if (string.IsNullOrEmpty(Name))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Category name is required."
                });
            }

            // Check if there are any Duplicates
            bool Exists = await _context.Categories.AnyAsync(c => c.Name == Name);

            // Do not Except Duplicate Values
            if (Exists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This category already exists."
                });
            }


            // Create the category using the submitted name.
            var category = new Category
            {
                Name = Name
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            // Return the new category.
            return Ok(new
            {
                status = 200,
                message = "Category created successfully.",
                data = category
            });
        }

        // DELETE: api/category/5
        // Only an Admin can delete a category.
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var category = _context.Categories.FirstOrDefault(c => c.Id == id);

            // Return an error if the category does not exist.
            if (category == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Category not found."
                });
            }

            // Remove the category from the database.
            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Category deleted successfully.",
            });
        }
    }
}
