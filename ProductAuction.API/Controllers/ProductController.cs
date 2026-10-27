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
        // Public listing — only approved and available products.
        [HttpGet]
        public async Task<IActionResult> GetAllProducts()
        {
            var products = await _context.Products
                .Where(p => p.ApprovalStatus == "Approved" && p.Status == "Available")
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProductListResponseDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    ShortSummary = p.ShortSummary,
                    StartingPrice = p.StartingPrice,
                    BuyoutPrice = p.BuyoutPrice,
                    ApprovalStatus = p.ApprovalStatus,

                    // main product image 
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == p.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),

                    CreatedAt = p.CreatedAt,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category.Name
                }).ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Products retrieved successfully.",
                data = products
            });
        }


        // GET: api/product/my-products
        // Lists the logged-in seller's own products (any status).
        [HttpGet("my-products")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> GetMyProducts()
        {
            // grab the user id from the JWT
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

                    // main product image
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == p.Id && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),

                    CreatedAt = p.CreatedAt,

                    // auction info 
                    AuctionStatus = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.Status)
                        .FirstOrDefault(),

                    AuctionStartTime = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.StartTime)
                        .FirstOrDefault(),

                    AuctionEndTime = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.EndTime)
                        .FirstOrDefault(),

                    AuctionId = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.Id)
                        .FirstOrDefault(),

                    AuctionBidCount = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.BidCount)
                        .FirstOrDefault(),

                    AuctionCurrentHighestBid = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.CurrentHighestBid)
                        .FirstOrDefault(),

                    AuctionCancellationReason = _context.Auctions
                        .Where(a => a.ProductId == p.Id)
                        .Select(a => a.CancellationReason)
                        .FirstOrDefault()
                }).ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Products retrieved successfully.",
                data = products
            });
        }


        // GET: api/product/my-products/{id}
        // Returns one product owned by the logged-in seller (any status).
        [HttpGet("my-products/{id}")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> GetMyProductById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
                return Unauthorized();

            var product = await _context.Products
                .Where(p => p.Id == id && p.SellerId == userId)
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
                    Status = p.Status,
                    CreatedAt = p.CreatedAt,

                    // all images for this product
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
                    message = "Product not found."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Product retrieved successfully.",
                data = product
            });
        }


        // GET: api/product/{id}
        // Public view of one approved + available product.
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetProductById(int id)
        {
            var product = await _context.Products
                .Where(p =>
                    p.Id == id &&
                    p.ApprovalStatus == "Approved" &&
                    p.Status == "Available")
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
                    Status = p.Status,
                    CreatedAt = p.CreatedAt,

                    // all images for this product
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
                    message = "Product not found."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Product retrieved successfully.",
                data = product
            });
        }


        // POST: api/product
        // Creates a new product as a Draft — no wallet reservation yet.
        [HttpPost]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> CreateProduct([FromForm] CreateProductDto dto)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // buyout price has to be at least the starting price
            if (dto.BuyoutPrice < dto.StartingPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Buyout price must be greater than or equal to starting price."
                });
            }

            // category has to exist
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.Id == dto.CategoryId);

            if (!categoryExists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Selected category does not exist."
                });
            }

            // create the product — drafts don't reserve any funds,
            // the deposit is taken when the product is submitted
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

            // upload any images that came with the request
            if (dto.Images != null && dto.Images.Count > 0)
            {
                for (int i = 0; i < dto.Images.Count; i++)
                {
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
        // Updates a draft or rejected product's details and images.
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromForm] UpdateProductDto dto)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the seller who owns it can edit
            if (product.SellerId != userId)
                return Forbid();

            // only drafts and rejected products are editable
            if (product.ApprovalStatus != "Draft" && product.ApprovalStatus != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft & Rejected products can be updated."
                });
            }

            // buyout price check
            if (dto.BuyoutPrice < dto.StartingPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Buyout price must be greater than or equal to starting price."
                });
            }

            // category check
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.Id == dto.CategoryId);

            if (!categoryExists)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Selected category does not exist."
                });
            }

            // update the basic fields
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

            // grab this product's existing images
            var images = await _context.ProductImages
                .Where(i => i.ProductId == id)
                .ToListAsync();

            // delete images the client asked to remove
            if (dto.DeleteImageIds != null && dto.DeleteImageIds.Count > 0)
            {
                var imagesToDelete = images
                    .Where(i => dto.DeleteImageIds.Contains(i.Id))
                    .ToList();

                // every requested id must belong to this product
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

            // upload any new images
            if (dto.NewImages != null && dto.NewImages.Count > 0)
            {
                foreach (var file in dto.NewImages)
                {
                    var imageUrl = await _imageUploadService.UploadImageAsync(file);

                    var newImage = new ProductImage
                    {
                        ProductId = product.Id,
                        ImageUrl = imageUrl,

                        // new uploads aren't main by default 
                        IsMain = false
                    };

                    _context.ProductImages.Add(newImage);
                    images.Add(newImage);
                }
            }

            // if the client explicitly chose a main image, promote it
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

                // clear all mains, then set the chosen one
                foreach (var img in images)
                {
                    img.IsMain = false;
                }

                chosen.IsMain = true;
            }

            // otherwise make sure at least one image is marked as main
            else if (images.Count > 0 && !images.Any(i => i.IsMain))
            {
                images[0].IsMain = true;
            }

            // rejected products go back to Draft when edited, so the seller can resubmit
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
        // Submits a draft product for admin approval (reserves the seller deposit).
        [HttpPost("{id}/submit-for-approval")]
        public async Task<IActionResult> SubmitForApproval(int id)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the owner can submit
            if (product.SellerId != userId)
                return Forbid();

            // only drafts can be submitted
            if (product.ApprovalStatus != "Draft")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft products can be submitted for approval."
                });
            }

            // find the seller's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Seller wallet not found."
                });
            }

            // deposit = 25% of starting price
            decimal reservationAmount = product.StartingPrice * 0.25m;

            // already-reserved funds can't be reused
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

            // reserve the deposit
            wallet.ReservedBalance += reservationAmount;

            // create the reservation
            var reservation = new WalletReservation
            {
                WalletId = wallet.Id,
                ProductId = product.Id,
                Type = "SellerDeposit",
                Amount = reservationAmount,
                Status = "Reserved"
            };

            _context.WalletReservations.Add(reservation);

            // send it to the admin review queue
            product.ApprovalStatus = "PendingApproval";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Product submitted for approval successfully."
            });
        }


        // POST: api/product/{id}/withdraw
        // Pulls a pending product back to Draft and releases the deposit.
        [HttpPost("{id}/withdraw")]
        public async Task<IActionResult> WithdrawSubmission(int id)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // find the product
            var product = await _context.Productss.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the owner can withdraw
            if (product.SellerId != userId)
                return Forbid();

            // can only withdraw while still waiting for review
            if (product.ApprovalStatus != "PendingApproval")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only products pending approval can be withdrawn."
                });
            }

            // find the active reservation for this product
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

                // sanity check before releasing
                if (wallet.ReservedBalance < reservation.Amount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Wallet reservation balance is inconsistent. Please contact an administrator."
                    });
                }

                wallet.ReservedBalance -= reservation.Amount;
                reservation.Status = "Released";
            }

            // back to draft
            product.ApprovalStatus = "Draft";
            product.RejectionReason = null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Product withdrawn and moved back to draft."
            });
        }


        // DELETE: api/product/{id}
        // Deletes a draft or rejected product (no reservation to release).
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the seller who owns it can delete
            if (product.SellerId != userId)
                return Forbid();

            // only drafts and rejected products can be deleted
            if (product.ApprovalStatus != "Draft" && product.ApprovalStatus != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft or rejected products can be deleted."
                });
            }

            // delete the product's images from storage first
            var images = await _context.ProductImages
                .Where(i => i.ProductId == product.Id)
                .ToListAsync();

            foreach (var image in images)
            {
                await _imageUploadService.DeleteImageAsync(image.ImageUrl);
            }

            // then remove the DB records
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
        // Admin: lists products waiting for approval.
        [HttpGet("pending-approval")]
        [Authorize(Roles = "Admin")]
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
                    Status = p.Status,
                    ApprovalStatus = p.ApprovalStatus,
                    CategoryName = p.Category.Name,
                    Condition = p.Condition,
                    RejectionReason = p.RejectionReason,

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
        // Admin: full details of a product waiting for approval.
        [HttpGet("pending-approval/{id}")]
        [Authorize(Roles = "Admin")]
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

                    // all images for this product
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
        // Admin approves or rejects a pending product.
        [HttpPut("{id}/approval")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ReviewProduct(int id, [FromBody] ProductApprovalDto dto)
        {
            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only pending products can be reviewed
            if (product.ApprovalStatus != "PendingApproval")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product has already been reviewed."
                });
            }

            // decision has to be Approved or Rejected
            if (dto.Status != "Approved" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Approved or Rejected."
                });
            }

            // rejection needs a reason
            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A rejection reason is required."
                });
            }

            // before approving, make sure the seller's deposit is actually there
            if (dto.Status == "Approved")
            {
                var reservation = await _context.WalletReservations
                    .FirstOrDefaultAsync(r =>
                        r.ProductId == product.Id &&
                        r.Type == "SellerDeposit" &&
                        r.Status == "Reserved");

                decimal requiredDeposit = product.StartingPrice * 0.25m;

                if (reservation == null || reservation.Amount != requiredDeposit)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The required seller deposit has not been reserved."
                    });
                }

                var wallet = await _context.Wallets
                    .FirstOrDefaultAsync(w =>
                        w.Id == reservation.WalletId &&
                        w.UserId == product.SellerId);

                if (wallet == null ||
                    wallet.ReservedBalance < reservation.Amount ||
                    wallet.Balance < reservation.Amount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "The seller's reserved deposit is invalid or insufficient."
                    });
                }
            }

            // apply the decision
            product.ApprovalStatus = dto.Status;

            if (dto.Status == "Rejected")
            {
                product.RejectionReason = dto.RejectionReason;

                // release the seller's deposit
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

                    if (wallet.ReservedBalance < reservation.Amount)
                    {
                        return BadRequest(new
                        {
                            status = 400,
                            message = "Wallet reservation balance is inconsistent. Please contact an administrator."
                        });
                    }

                    wallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = "Released";
                }
            }
            else
            {
                // approved — reservation stays in place, and we spin up an auction
                product.RejectionReason = null;

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