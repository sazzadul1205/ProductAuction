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

        public CategoryController(AppDbContext context)
        {
            _context = context;
        }


        // GET: api/category
        // Anyone can view the available categories.
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories.ToListAsync();

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

            // name can't be empty
            if (string.IsNullOrEmpty(name))
            {
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