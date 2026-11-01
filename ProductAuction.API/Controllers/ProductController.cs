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
        private readonly ILogger<ProductController> _logger;

        public ProductController(
            AppDbContext context,
            IImageUploadService imageUploadService,
            ILogger<ProductController> logger)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _logger = logger;
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
                // _logger.LogWarning("GetMyProducts: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("GetMyProducts called by seller {SellerId}.", userId);

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


            // System.Diagnostics.Debug.Assert(products.All(p => p.Id > 0));

            // _logger.LogInformation("GetMyProducts returned {Count} product(s) for seller {SellerId}.", products.Count, userId);

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
            {
                // _logger.LogWarning("GetMyProductById: user identity missing from token.");

                return Unauthorized();
            }

            // _logger.LogInformation("GetMyProductById called for product #{ProductId} by seller {SellerId}.", id, userId);

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
                // _logger.LogWarning("GetMyProductById: product #{ProductId} not found for seller {SellerId}.", id, userId);

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
                // _logger.LogWarning("CreateProduct: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("CreateProduct called by seller {SellerId} (title='{Title}', images={ImageCount}).",
            //     userId, dto.Title, dto.Images?.Count ?? 0);

            // buyout price has to be at least the starting price
            if (dto.BuyoutPrice < dto.StartingPrice)
            {
                // _logger.LogWarning("CreateProduct: buyout {Buyout} < starting {Starting} for seller {SellerId}.",
                //     dto.BuyoutPrice, dto.StartingPrice, userId);

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
                // _logger.LogWarning("CreateProduct: category #{CategoryId} does not exist.", dto.CategoryId);

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

                    // _logger.LogDebug("CreateProduct: image {Index}/{Total} uploaded for product #{ProductId} (isMain={IsMain}).",
                    //     i + 1, dto.Images.Count, product.Id, productImage.IsMain);
                }

                await _context.SaveChangesAsync();
            }


            // System.Diagnostics.Debug.Assert(product.Id > 0);
            // System.Diagnostics.Debug.Assert(product.ApprovalStatus == "Draft");
            // System.Diagnostics.Debug.Assert(dto.Images == null || dto.Images.Count == 0
            //     || await _context.ProductImages.CountAsync(i => i.ProductId == product.Id) == dto.Images.Count);

            // _logger.LogInformation("CreateProduct: draft product #{ProductId} created for seller {SellerId}.", product.Id, userId);

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
                // _logger.LogWarning("UpdateProduct: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("UpdateProduct called for product #{ProductId} by user {UserId}.", id, userId);

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                // _logger.LogWarning("UpdateProduct: product #{ProductId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the seller who owns it can edit
            if (product.SellerId != userId)
            {
                // _logger.LogWarning("UpdateProduct: user {UserId} is not the owner of product #{ProductId}.", userId, id);

                return Forbid();
            }

            // only drafts and rejected products are editable
            if (product.ApprovalStatus != "Draft" && product.ApprovalStatus != "Rejected")
            {
                // _logger.LogWarning("UpdateProduct: product #{ProductId} has status {Status}; only Draft/Rejected are editable.",
                //     id, product.ApprovalStatus);

                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft & Rejected products can be updated."
                });
            }

            // buyout price check
            if (dto.BuyoutPrice < dto.StartingPrice)
            {
                // _logger.LogWarning("UpdateProduct: buyout {Buyout} < starting {Starting} for product #{ProductId}.",
                //     dto.BuyoutPrice, dto.StartingPrice, id);

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
                // _logger.LogWarning("UpdateProduct: category #{CategoryId} does not exist.", dto.CategoryId);

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
                    // _logger.LogWarning("UpdateProduct: some delete-image IDs do not belong to product #{ProductId}.", id);

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

                    // _logger.LogDebug("UpdateProduct: image #{ImageId} deleted from product #{ProductId}.", image.Id, id);
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

                // _logger.LogDebug("UpdateProduct: {Count} new image(s) uploaded for product #{ProductId}.",
                //     dto.NewImages.Count, id);
            }

            // if the client explicitly chose a main image, promote it
            if (dto.MainImageId.HasValue)
            {
                var chosen = images.FirstOrDefault(i => i.Id == dto.MainImageId.Value);

                if (chosen == null)
                {
                    // _logger.LogWarning("UpdateProduct: main image #{ImageId} does not belong to product #{ProductId}.",
                    //     dto.MainImageId.Value, id);

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

                // _logger.LogInformation("UpdateProduct: rejected product #{ProductId} reset to Draft for resubmission.", id);
            }

            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(images.Count == 0 || images.Any(i => i.IsMain));

            // _logger.LogInformation("UpdateProduct: product #{ProductId} updated by seller {SellerId}.", id, userId);

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
                // _logger.LogWarning("SubmitForApproval: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("SubmitForApproval called for product #{ProductId} by user {UserId}.", id, userId);

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                // _logger.LogWarning("SubmitForApproval: product #{ProductId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the owner can submit
            if (product.SellerId != userId)
            {
                // _logger.LogWarning("SubmitForApproval: user {UserId} is not the owner of product #{ProductId}.", userId, id);

                return Forbid();
            }

            // only drafts can be submitted
            if (product.ApprovalStatus != "Draft")
            {
                // _logger.LogWarning("SubmitForApproval: product #{ProductId} has status {Status}; only Draft is submittable.",
                //     id, product.ApprovalStatus);

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
                // _logger.LogError("SubmitForApproval: seller wallet for user {UserId} not found.", userId);

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
                // _logger.LogWarning("SubmitForApproval: insufficient balance for user {UserId} (available={Available}, required={Required}).",
                //     userId, availableBalance, reservationAmount);

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


            // System.Diagnostics.Debug.Assert(wallet.ReservedBalance >= reservationAmount);
            // System.Diagnostics.Debug.Assert(product.ApprovalStatus == "PendingApproval");

            // _logger.LogInformation("SubmitForApproval: product #{ProductId} submitted with {Deposit} reserved.",
            //     id, reservationAmount);

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
                // _logger.LogWarning("WithdrawSubmission: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("WithdrawSubmission called for product #{ProductId} by user {UserId}.", id, userId);

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                // _logger.LogWarning("WithdrawSubmission: product #{ProductId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the owner can withdraw
            if (product.SellerId != userId)
            {
                // _logger.LogWarning("WithdrawSubmission: user {UserId} is not the owner of product #{ProductId}.", userId, id);

                return Forbid();
            }

            // can only withdraw while still waiting for review
            if (product.ApprovalStatus != "PendingApproval")
            {
                // _logger.LogWarning("WithdrawSubmission: product #{ProductId} has status {Status}; only PendingApproval can be withdrawn.",
                //     id, product.ApprovalStatus);

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
                    // _logger.LogError("WithdrawSubmission: wallet #{WalletId} for reservation #{ReservationId} not found.",
                    //     reservation.WalletId, reservation.Id);

                    return BadRequest(new
                    {
                        status = 400,
                        message = "Wallet linked to the reservation was not found. Please contact an administrator."
                    });
                }

                // sanity check before releasing
                if (wallet.ReservedBalance < reservation.Amount)
                {
                    // _logger.LogError("WithdrawSubmission: wallet #{WalletId} reserved balance inconsistent ({Reserved} < {Amount}).",
                    //     wallet.Id, wallet.ReservedBalance, reservation.Amount);

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


            // System.Diagnostics.Debug.Assert(product.ApprovalStatus == "Draft");
            // System.Diagnostics.Debug.Assert(!await _context.WalletReservations.AnyAsync(r =>
            //     r.ProductId == id && r.Type == "SellerDeposit" && r.Status == "Reserved"));

            // _logger.LogInformation("WithdrawSubmission: product #{ProductId} withdrawn back to Draft.", id);

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
                // _logger.LogWarning("DeleteProduct: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("DeleteProduct called for product #{ProductId} by user {UserId}.", id, userId);

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                // _logger.LogWarning("DeleteProduct: product #{ProductId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only the seller who owns it can delete
            if (product.SellerId != userId)
            {
                // _logger.LogWarning("DeleteProduct: user {UserId} is not the owner of product #{ProductId}.", userId, id);

                return Forbid();
            }

            // only drafts and rejected products can be deleted
            if (product.ApprovalStatus != "Draft" && product.ApprovalStatus != "Rejected")
            {
                // _logger.LogWarning("DeleteProduct: product #{ProductId} has status {Status}; only Draft/Rejected can be deleted.",
                //     id, product.ApprovalStatus);

                return BadRequest(new
                {
                    status = 400,
                    message = "Only draft or rejected products can be deleted."
                });
            }

            // load the product's images
            var images = await _context.ProductImages
                .Where(i => i.ProductId == product.Id)
                .ToListAsync();

            // remove the DB records first
            _context.ProductImages.RemoveRange(images);
            _context.Products.Remove(product);

            await _context.SaveChangesAsync();

            // only delete files from storage after the DB commit succeeds.
            // if the commit fails, files are preserved and can be retried.
            foreach (var image in images)
            {
                await _imageUploadService.DeleteImageAsync(image.ImageUrl);

                // _logger.LogDebug("DeleteProduct: image #{ImageId} removed from storage for product #{ProductId}.", image.Id, id);
            }


            // System.Diagnostics.Debug.Assert(!await _context.Products.AnyAsync(p => p.Id == id));

            // _logger.LogInformation("DeleteProduct: product #{ProductId} and {ImageCount} image(s) deleted.", id, images.Count);

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
            // _logger.LogInformation("GetPendingApprovalProducts called.");

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

            // _logger.LogInformation("GetPendingApprovalProducts returned {Count} product(s).", products.Count);


            // System.Diagnostics.Debug.Assert(products.All(p => p.ApprovalStatus == "PendingApproval"));

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
            // _logger.LogInformation("GetPendingApprovalProductById called for product #{ProductId}.", id);

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
                // _logger.LogWarning("GetPendingApprovalProductById: product #{ProductId} not found in PendingApproval state.", id);

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
            // _logger.LogInformation("ReviewProduct called for product #{ProductId} with decision '{Status}'.", id, dto.Status);

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                // _logger.LogWarning("ReviewProduct: product #{ProductId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only pending products can be reviewed
            if (product.ApprovalStatus != "PendingApproval")
            {
                // _logger.LogWarning("ReviewProduct: product #{ProductId} has status {Status}; only PendingApproval is reviewable.",
                //     id, product.ApprovalStatus);

                return BadRequest(new
                {
                    status = 400,
                    message = "This product has already been reviewed."
                });
            }

            // decision has to be Approved or Rejected
            if (dto.Status != "Approved" && dto.Status != "Rejected")
            {
                // _logger.LogWarning("ReviewProduct: invalid decision '{Status}' for product #{ProductId}.", dto.Status, id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Approved or Rejected."
                });
            }

            // rejection needs a reason
            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
            {
                // _logger.LogWarning("ReviewProduct: rejection reason missing for product #{ProductId}.", id);

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
                    // _logger.LogError("ReviewProduct: seller deposit missing or mismatched for product #{ProductId} (expected={Expected}).",
                    //     id, requiredDeposit);

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
                    // _logger.LogError("ReviewProduct: seller wallet #{WalletId} invalid or insufficient for product #{ProductId}.",
                    //     reservation.WalletId, id);

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
                        // _logger.LogError("ReviewProduct (Reject): wallet #{WalletId} for reservation #{ReservationId} not found.",
                        //     reservation.WalletId, reservation.Id);

                        return BadRequest(new
                        {
                            status = 400,
                            message = "Wallet linked to the reservation was not found. Please contact an administrator."
                        });
                    }

                    if (wallet.ReservedBalance < reservation.Amount)
                    {
                        // _logger.LogError("ReviewProduct (Reject): wallet #{WalletId} reserved balance inconsistent ({Reserved} < {Amount}).",
                        //     wallet.Id, wallet.ReservedBalance, reservation.Amount);

                        return BadRequest(new
                        {
                            status = 400,
                            message = "Wallet reservation balance is inconsistent. Please contact an administrator."
                        });
                    }

                    wallet.ReservedBalance -= reservation.Amount;
                    reservation.Status = "Released";
                }

                // _logger.LogInformation("ReviewProduct: product #{ProductId} rejected; seller deposit released.", id);
            }
            else
            {
                // approved — reservation stays in place, and we spin up an auction
                product.RejectionReason = null;
                product.Status = "Available";

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

                    // _logger.LogInformation("ReviewProduct: auction created for approved product #{ProductId}.", id);
                }

                // _logger.LogInformation("ReviewProduct: product #{ProductId} approved; deposit retained.", id);
            }

            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(product.ApprovalStatus == dto.Status);
            // System.Diagnostics.Debug.Assert(dto.Status != "Approved"
            //     || await _context.Auctions.AnyAsync(a => a.ProductId == product.Id));

            // _logger.LogInformation("ReviewProduct: product #{ProductId} reviewed with decision '{Status}'.", id, dto.Status);

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