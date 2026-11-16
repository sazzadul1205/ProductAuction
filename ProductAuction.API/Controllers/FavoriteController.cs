using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Common;
using ProductAuction.API.DTOs.Favorite;
using ProductAuction.API.Models;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FavoriteController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FavoriteController(AppDbContext context)
        {
            _context = context;
        }

        private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        // POST: api/favorite/{auctionId}
        [HttpPost("{auctionId:int}")]
        public async Task<IActionResult> AddFavorite(int auctionId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { status = 401, message = "Unauthorized." });

            var auctionExists = await _context.Auctions.AnyAsync(a => a.Id == auctionId);
            if (!auctionExists)
                return NotFound(new { status = 404, message = "Auction not found." });

            var existing = await _context.Favorites
                .FirstOrDefaultAsync(f => f.UserId == userId && f.AuctionId == auctionId);

            if (existing != null)
            {
                return Ok(new
                {
                    status = 200,
                    message = "Already in favorites.",
                    data = new { auctionId, isFavorited = true, favoritedAt = existing.CreatedAt }
                });
            }

            var favorite = new Favorite
            {
                UserId = userId,
                AuctionId = auctionId,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Favorites.Add(favorite);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                var dup = await _context.Favorites.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.AuctionId == auctionId);
                if (dup != null)
                {
                    return Ok(new
                    {
                        status = 200,
                        message = "Already in favorites.",
                        data = new { auctionId, isFavorited = true, favoritedAt = dup.CreatedAt }
                    });
                }
                throw;
            }

            return Ok(new
            {
                status = 200,
                message = "Added to favorites.",
                data = new { auctionId, isFavorited = true, favoritedAt = favorite.CreatedAt }
            });
        }

        // DELETE: api/favorite/{auctionId}
        [HttpDelete("{auctionId:int}")]
        public async Task<IActionResult> RemoveFavorite(int auctionId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { status = 401, message = "Unauthorized." });

            var favorite = await _context.Favorites
                .FirstOrDefaultAsync(f => f.UserId == userId && f.AuctionId == auctionId);

            if (favorite != null)
            {
                _context.Favorites.Remove(favorite);
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                status = 200,
                message = "Removed from favorites.",
                data = new { auctionId, isFavorited = false }
            });
        }

        // GET: api/favorite
        [HttpGet]
        public async Task<IActionResult> GetFavorites([FromQuery] PaginationQueryDto query)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { status = 401, message = "Unauthorized." });

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

            var baseQuery = _context.Favorites
                .AsNoTracking()
                .Where(f => f.UserId == userId);

            var totalCount = await baseQuery.CountAsync();

            var items = await baseQuery
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new FavoriteAuctionDto
                {
                    FavoriteId = f.Id,
                    FavoritedAt = f.CreatedAt,
                    AuctionId = f.AuctionId,
                    ProductTitle = f.Auction.Product.Title,
                    MainImageUrl = _context.ProductImages
                        .Where(i => i.ProductId == f.Auction.ProductId && i.IsMain)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                    CategoryId = f.Auction.Product.CategoryId,
                    CategoryName = f.Auction.Product.Category.Name,
                    StartingPrice = f.Auction.Product.StartingPrice,
                    BuyoutPrice = f.Auction.Product.BuyoutPrice,
                    CurrentHighestBid = f.Auction.CurrentHighestBid,
                    BidCount = f.Auction.BidCount,
                    ViewCount = f.Auction.ViewCount,
                    IsFeatured = f.Auction.IsFeatured,
                    StartTime = f.Auction.StartTime,
                    EndTime = f.Auction.EndTime,
                    Status = f.Auction.Status.ToString(),
                    Result = f.Auction.Result != null ? f.Auction.Result.ToString() : null
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Favorites retrieved successfully.",
                data = items,
                pagination = new { page, pageSize, totalCount }
            });
        }

        // GET: api/favorite/check/{auctionId}
        [HttpGet("check/{auctionId:int}")]
        public async Task<IActionResult> CheckFavorite(int auctionId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { status = 401, message = "Unauthorized." });

            var isFavorited = await _context.Favorites
                .AsNoTracking()
                .AnyAsync(f => f.UserId == userId && f.AuctionId == auctionId);

            return Ok(new
            {
                status = 200,
                message = "Favorite status retrieved.",
                data = new { auctionId, isFavorited }
            });
        }

        // GET: api/favorite/ids?auctionIds=1&auctionIds=2
        [HttpGet("ids")]
        public async Task<IActionResult> GetFavoriteIds([FromQuery] List<int> auctionIds)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { status = 401, message = "Unauthorized." });

            if (auctionIds == null || auctionIds.Count == 0)
            {
                return Ok(new { status = 200, message = "Favorite ids retrieved.", data = new { ids = Array.Empty<int>() } });
            }

            var ids = auctionIds.Distinct().Take(100).ToList();
            var favorited = await _context.Favorites
                .AsNoTracking()
                .Where(f => f.UserId == userId && ids.Contains(f.AuctionId))
                .Select(f => f.AuctionId)
                .ToListAsync();

            return Ok(new { status = 200, message = "Favorite ids retrieved.", data = new { ids = favorited } });
        }
    }
}
