using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Product;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/product
        // Public list of approved products.
        [HttpGet]
        public async Task<IActionResult> GetAllProducts()
        {
            var products = await _context.Products
                 .Where(p => p.ApprovalStatus == "Approved")
                 .OrderByDescending(p => p.CreatedAt)
                 .Select(p => new ProductListResponseDto
                 {
                     Id = p.Id,
                     Title = p.Title,
                     ShortSummary = p.ShortSummary,
                     StartingPrice = p.StartingPrice,
                     BuyoutPrice = p.BuyoutPrice,
                     ApprovalStatus = p.ApprovalStatus,
                     MainImageUrl = _context.ProductImages
                         .Where(i => i.ProductId == p.Id && i.IsMain)
                         .Select(i => i.ImageUrl)
                         .FirstOrDefault(),
                     CreatedAt = p.CreatedAt
                 }).ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Products retrieved successfully.",
                data = products
            });
        }

        // GET: api/product/my-products
        // List the logged-in seller's own products.
        [HttpGet("my-products")]
        public async Task<IActionResult> GetMyProducts()
        {
            // Read the user's ID from the validated JWT token.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            var products = await _context.Products
                .Where(p => p.SellerId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProductListResponseDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    ShortSummary = p.ShortSummary,
                    StartingPrice = p.StartingPrice,
                    BuyoutPrice = p.BuyoutPrice,
                    ApprovalStatus = p.ApprovalStatus,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == p.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                    CreatedAt = p.CreatedAt
                }).ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Products retrieved successfully.",
                data = products
            });
        }

        // GET: api/product/{id}
        // Retrieve one approved product with its details and images.
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetProductById(int id)
        {
            // Query the product with this id and project it into a response DTO.
            var product = await _context.Products
                .Where(p => p.Id == id)
                .Select(p => new ProductResponseDto
                {
                    Id = p.Id,
                    SellerId = p.SellerId,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category.Name,
                    Title = p.Title,
                    ShortSummary = p.ShortSummary,
                    Description = p.Description,
                    Brand = p.Brand,
                    ModelNumber = p.ModelNumber,
                    Condition = p.Condition,
                    PurchaseDate = p.PurchaseDate,
                    UsageHistory = p.UsageHistory,
                    ReasonForSelling = p.ReasonForSelling,
                    KnownIssues = p.KnownIssues,
                    StartingPrice = p.StartingPrice,
                    BuyoutPrice = p.BuyoutPrice,
                    ApprovalStatus = p.ApprovalStatus,
                    RejectionReason = p.RejectionReason,
                    CreatedAt = p.CreatedAt,

                    // Load the images that belong to this product.
                    Images = _context.ProductImages
                        .Where(i => i.ProductId == p.Id)
                        .Select(i => new ProductImageResponseDto
                        {
                            Id = i.Id,
                            ImageUrl = i.ImageUrl,
                            IsMain = i.IsMain
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            // If no product matches this id, return not found.
            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Return the product with a success message.
            return Ok(new
            {
                status = 200,
                message = "Product retrieved successfully.",
                data = product
            });
        }
    }
}
