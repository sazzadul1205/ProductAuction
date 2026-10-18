using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Product;
using ProductAuction.API.Models;
using ProductAuction.API.Services;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IImageUploadService _imageUploadService;

        public ProductController(
            AppDbContext context,
            IImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }

        // GET: api/product
        // Public list of Approved products.
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
            // Query the product with this id (approved only) and project it into a response DTO.
            var product = await _context.Products
                .Where(p => p.Id == id && p.ApprovalStatus == "Approved")
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

            // If no approved product matches this id, return not found.
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


        // POST: api/product
        // Create a New Product (saved as a Draft — no wallet reservation yet).
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromForm] CreateProductDto dto)
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

            // Buyout price must not be lower than starting price.
            if (dto.BuyoutPrice < dto.StartingPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Buyout price must be greater than or equal to starting price."
                });
            }

            // Check that the selected category exists.
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);

            if (!categoryExists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Selected category does not exist."
                });
            }

            // Create the product. SellerId comes from the JWT.
            // Drafts do NOT reserve any wallet funds; the deposit is taken on submission.
            var product = new Product
            {
                SellerId = userId,
                CategoryId = dto.CategoryId,
                Title = dto.Title,
                ShortSummary = dto.ShortSummary ?? string.Empty,
                Description = dto.Description,
                Brand = dto.Brand,
                ModelNumber = dto.ModelNumber,
                Condition = dto.Condition,
                PurchaseDate = dto.PurchaseDate,
                UsageHistory = dto.UsageHistory ?? string.Empty,
                ReasonForSelling = dto.ReasonForSelling ?? string.Empty,
                KnownIssues = dto.KnownIssues ?? string.Empty,
                StartingPrice = dto.StartingPrice,
                BuyoutPrice = dto.BuyoutPrice,
                ApprovalStatus = "Draft",
                CreatedAt = DateTime.UtcNow
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            // Upload product images, if provided.
            if (dto.Images != null && dto.Images.Count > 0)
            {
                // Loop through each uploaded image.
                for (int i = 0; i < dto.Images.Count; i++)
                {
                    // Generate Image URL via _imageUploadService 
                    var imageUrl = await _imageUploadService.UploadImageAsync(dto.Images[i]);

                    var productImage = new ProductImage
                    {
                        ProductId = product.Id,
                        ImageUrl = imageUrl,
                        IsMain = dto.MainImageIndex == i
                    };

                    _context.ProductImages.Add(productImage);
                }

                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                status = 200,
                message = "Draft product created successfully."
            });
        }


        // PUT: api/product/{id}
        // Update product details and images (drafts only).
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromForm] UpdateProductDto dto)
        {
            // Get the logged-in user's ID from the JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the product.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Only the product's seller can update it.
            if (product.SellerId != userId)
            {
                return Forbid();
            }

            // Only draft products can be edited.
            if (product.ApprovalStatus != "Draft" && product.ApprovalStatus != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft & Rejected products can be updated."
                });
            }



            // Validate prices.
            if (dto.BuyoutPrice < dto.StartingPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Buyout price must be greater than or equal to starting price."
                });
            }

            // Check that the category exists.
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);

            if (!categoryExists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Selected category does not exist."
                });
            }

            // Update product details.
            product.CategoryId = dto.CategoryId;
            product.Title = dto.Title;
            product.ShortSummary = dto.ShortSummary ?? string.Empty;
            product.Description = dto.Description;
            product.Brand = dto.Brand;
            product.ModelNumber = dto.ModelNumber;
            product.Condition = dto.Condition;
            product.PurchaseDate = dto.PurchaseDate;
            product.UsageHistory = dto.UsageHistory ?? string.Empty;
            product.ReasonForSelling = dto.ReasonForSelling ?? string.Empty;
            product.KnownIssues = dto.KnownIssues ?? string.Empty;
            product.StartingPrice = dto.StartingPrice;
            product.BuyoutPrice = dto.BuyoutPrice;

            // Get the product's existing images.
            var images = await _context.ProductImages.Where(i => i.ProductId == id).ToListAsync();

            // Delete selected images.
            if (dto.DeleteImageIds != null && dto.DeleteImageIds.Count > 0)
            {
                // Make sure every requested image belongs to this product.
                var imagesToDelete = images.Where(i => dto.DeleteImageIds.Contains(i.Id)).ToList();

                if (imagesToDelete.Count != dto.DeleteImageIds.Distinct().Count())
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "One or more image IDs do not belong to this product."
                    });
                }

                foreach (var image in imagesToDelete)
                {
                    await _imageUploadService.DeleteImageAsync(image.ImageUrl);
                    _context.ProductImages.Remove(image);
                    images.Remove(image);
                }
            }

            // Upload new images.
            if (dto.NewImages != null && dto.NewImages.Count > 0)
            {
                foreach (var file in dto.NewImages)
                {
                    var imageUrl = await _imageUploadService.UploadImageAsync(file);

                    var newImage = new ProductImage
                    {
                        ProductId = product.Id,
                        ImageUrl = imageUrl,
                        // New uploads are not main by default; the main is decided below.
                        IsMain = false
                    };

                    _context.ProductImages.Add(newImage);
                    images.Add(newImage);
                }
            }

            // If the client explicitly chose a main image, promote it (must belong to this product).
            if (dto.MainImageId.HasValue)
            {
                var chosen = images.FirstOrDefault(i => i.Id == dto.MainImageId.Value);

                if (chosen == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The selected main image does not belong to this product."
                    });
                }

                // Clear all mains, then set the chosen one.
                foreach (var img in images)
                {
                    img.IsMain = false;
                }

                chosen.IsMain = true;
            }

            // Ensure one image is marked as the main image, if any exist.
            else if (images.Count > 0 && !images.Any(i => i.IsMain))
            {
                images[0].IsMain = true;
            }

            // If the product was rejected and is being edited, move it back to Draft
            // so the seller can resubmit.
            if (product.ApprovalStatus == "Rejected")
            {
                product.ApprovalStatus = "Draft";
                product.RejectionReason = null;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Draft product and images updated successfully."
            });
        }


        // POST: api/product/{id}/submit-for-approval
        // Submit a draft product for admin approval.
        [HttpPost("{id}/submit-for-approval")]
        public async Task<IActionResult> SubmitForApproval(int id)
        {
            // Get the logged-in seller's ID from the JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the seller's product.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Only the owner can submit the product.
            if (product.SellerId != userId)
            {
                return Forbid();
            }

            // Only draft products can be submitted.
            if (product.ApprovalStatus != "Draft")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft products can be submitted for approval."
                });
            }

            // Find the seller's wallet.
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Seller wallet not found."
                });
            }

            // Calculate the required deposit: 25% of starting price.
            decimal reservationAmount = product.StartingPrice * 0.25m;

            // Reserved funds cannot be used again.
            decimal availableBalance = wallet.Balance - wallet.ReservedBalance;

            if (availableBalance < reservationAmount)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Insufficient available wallet balance.",
                    requiredAmount = reservationAmount,
                    availableBalance
                });
            }

            // Reserve the seller's deposit.
            wallet.ReservedBalance += reservationAmount;

            // Create a reservation linked to this product.
            var reservation = new WalletReservation
            {
                WalletId = wallet.Id,
                ProductId = product.Id,
                Type = "SellerDeposit",
                Amount = reservationAmount,
                Status = "Reserved"
            };

            _context.WalletReservations.Add(reservation);

            // Send the product to the admin review queue.
            product.ApprovalStatus = "PendingApproval";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Product submitted for approval successfully."
            });
        }

        // POST: api/product/{id}/withdraw
        // Withdraw a pending product back to Draft (only while it hasn't been reviewed).
        [HttpPost("{id}/withdraw")]
        public async Task<IActionResult> WithdrawSubmission(int id)
        {
            // Get the logged-in seller's ID from the JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the product.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Only the owner can withdraw the product.
            if (product.SellerId != userId)
            {
                return Forbid();
            }

            // Only products that are still waiting for review can be withdrawn.
            // Once approved or rejected, the product can no longer be toggled.
            if (product.ApprovalStatus != "PendingApproval")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only products pending approval can be withdrawn."
                });
            }

            // Find the active reservation linked to this product.
            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == product.Id &&
                    r.Type == "SellerDeposit" &&
                    r.Status == "Reserved");

            if (reservation != null)
            {
                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

                if (wallet == null)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Wallet linked to the reservation was not found. Please contact an administrator."
                    });
                }

                // Ensure the wallet has enough reserved funds to release.
                if (wallet.ReservedBalance < reservation.Amount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Wallet reservation balance is inconsistent. Please contact an administrator."
                    });
                }

                // Release the funds and update the reservation.
                wallet.ReservedBalance -= reservation.Amount;
                reservation.Status = "Released";
            }

            // Flip the product back to Draft so the seller can edit it again.
            product.ApprovalStatus = "Draft";

            // Clear any leftover rejection reason from a previous cycle.
            product.RejectionReason = null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Product withdrawn and moved back to draft."
            });
        }

        // DELETE: api/product/{id}
        // Delete a draft product (no reservation to release).
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            // Get the logged-in user's ID from the JWT.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the product.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Only the product's seller can delete it.
            if (product.SellerId != userId)
            {
                return Forbid();
            }

            // Only draft products can be deleted.
            if (product.ApprovalStatus != "Draft" && product.ApprovalStatus != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft or rejected products can be deleted."
                });
            }

            // Find and delete the product's images.
            var images = await _context.ProductImages.Where(i => i.ProductId == product.Id).ToListAsync();

            foreach (var image in images)
            {
                await _imageUploadService.DeleteImageAsync(image.ImageUrl);
            }

            // Remove the image records and product.
            _context.ProductImages.RemoveRange(images);
            _context.Products.Remove(product);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Draft product deleted successfully."
            });
        }


        // GET: api/product/pending-approval
        // Get products waiting for admin approval.
        [HttpGet("pending-approval")]
        public async Task<IActionResult> GetPendingApprovalProducts()
        {
            var products = await _context.Products
                .Where(p => p.ApprovalStatus == "PendingApproval")
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
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Pending approval products retrieved successfully.",
                data = products
            });
        }


        // GET: api/product/pending-approval/{id}
        // Get full details of a product waiting for approval.
        [HttpGet("pending-approval/{id}")]
        public async Task<IActionResult> GetPendingApprovalProductById(int id)
        {
            var product = await _context.Products
                .Where(p => p.Id == id && p.ApprovalStatus == "PendingApproval")
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

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Pending approval product not found."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Pending approval product retrieved successfully.",
                data = product
            });
        }


        // PUT: api/product/{id}/approval
        // Admin approves or rejects a product.
        [HttpPut("{id}/approval")]
        public async Task<IActionResult> ReviewProduct(int id, [FromBody] ProductApprovalDto dto)
        {
            // Find the product.
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // Only pending products can be reviewed.
            if (product.ApprovalStatus != "PendingApproval")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product has already been reviewed."
                });
            }

            // Check the requested decision.
            if (dto.Status != "Approved" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Approved or Rejected."
                });
            }

            // A rejected product must have a reason.
            if (dto.Status == "Rejected" && string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A rejection reason is required."
                });
            }

            // Update the product's approval status.
            product.ApprovalStatus = dto.Status;

            if (dto.Status == "Rejected")
            {
                product.RejectionReason = dto.RejectionReason;

                // Release the seller's deposit reservation on rejection.
                var reservation = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.ProductId == product.Id &&
                        r.Type == "SellerDeposit" &&
                        r.Status == "Reserved");

                if (reservation != null && reservation.Status == "Reserved")
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == reservation.WalletId);

                    if (wallet == null)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Wallet linked to the reservation was not found. Please contact an administrator."
                        });
                    }

                    // Check that the wallet has enough reserved funds.
                    if (wallet.ReservedBalance < reservation.Amount)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Wallet reservation balance is inconsistent. Please contact an administrator."
                        });
                    }

                    // Release the funds and update the reservation.
                    wallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = "Released";
                }
            }
            else
            {
                // Approved — the reservation stays in place.
                product.RejectionReason = null;

                // Create an auction automatically when the product is approved.
                var auctionExists = await _context.Auctions.AnyAsync(a => a.ProductId == product.Id);

                if (!auctionExists)
                {
                    var auction = new Auction
                    {
                        ProductId = product.Id,
                        SellerId = product.SellerId,
                        Status = "Upcoming"
                    };

                    _context.Auctions.Add(auction);
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = dto.Status == "Approved"
                    ? "Product approved successfully."
                    : "Product rejected successfully."
            });
        }
    }
}