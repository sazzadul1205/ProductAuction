using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Bid;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;
using ProductAuction.API.Services;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BidController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BidController> _logger;
        private readonly INotificationService _notificationService;

        public BidController(AppDbContext context, ILogger<BidController> logger, INotificationService notificationService)
        {
            _context = context;
            _logger = logger;
            _notificationService = notificationService;
        }


        // POST: api/bid
        // Places a bid on an active auction.
        [HttpPost]
        [Authorize(Roles = "Buyer")]
        public async Task<IActionResult> PlaceBid(PlaceBidDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }


            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == dto.AuctionId);
            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);
            if (product == null)
            {

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            if (product.SellerId == userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    status = 403,
                    message = "Sellers cannot bid on or buy their own auction."
                });
            }

            if (auction.Status != AuctionStatus.Active)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Bids can only be placed on active auctions."
                });
            }


            if (auction.EndTime == null || auction.EndTime <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This auction has ended."
                });
            }

            // product has to be approved and available
            if (product.ApprovalStatus != ProductApprovalStatus.Approved || product.Status != ProductStatus.Available)
            {

                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved or available for bidding."
                });
            }

            // bids must not be at or above the buyout price
            if (product.BuyoutPrice > 0 && dto.Amount >= product.BuyoutPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Your bid must be below the buyout price. Use the buyout Button."
                });
            }

            // first bid has to meet the starting price
            if (dto.Amount < product.StartingPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = $"Minimum bid is {product.StartingPrice:F2}."
                });
            }

            // next bids need to clear the minimum increment
            if (auction.CurrentHighestBid.HasValue)
            {
                var highestBid = auction.CurrentHighestBid.Value;

                var increment = GetBidIncrement(highestBid, product.StartingPrice, product.BuyoutPrice);

                var minimumBid = highestBid + increment;

                if (dto.Amount < minimumBid)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = $"Minimum bid is {minimumBid:F2}."
                    });
                }
            }


            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet not found."
                });
            }

            // see if the buyer already has funds locked for this auction
            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.AuctionId == auction.Id &&
                    r.WalletId == wallet.Id &&
                    r.Type == ReservationType.BuyerFunds &&
                    r.Status == ReservationStatus.Reserved);

            // reserve 25% of the starting price,
            if (reservation == null)
            {
                var requiredAmount = product.StartingPrice * 0.25m;
                var availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "You need at least 25% of the starting price available in your wallet to participate."
                    });
                }

                wallet.ReservedBalance += requiredAmount;

                reservation = new WalletReservation
                {
                    WalletId = wallet.Id,
                    ProductId = auction.ProductId,
                    AuctionId = auction.Id,
                    Type = ReservationType.BuyerFunds,
                    Amount = requiredAmount,
                    Status = ReservationStatus.Reserved
                };

                _context.WalletReservations.Add(reservation);
            }

            var bid = new Bid
            {
                AuctionId = auction.Id,
                BidderId = userId,
                Amount = dto.Amount,
                PlacedAt = DateTime.UtcNow
            };

            var previousHighestBidderId = auction.CurrentHighestBidderId;
            var previousHighestBid = auction.CurrentHighestBid;

            _context.Bids.Add(bid);

            // the auction's highest bid info
            auction.CurrentHighestBid = dto.Amount;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(previousHighestBidderId)
                && previousHighestBidderId != userId
                && previousHighestBid.HasValue)
            {
                await _notificationService.CreateNotificationAsync(
                    previousHighestBidderId,
                    "You have been outbid",
                    $"You were outbid on \"{product.Title}\". The new highest bid is {dto.Amount:F2}.",
                    $"/auctions/{auction.Id}");
            }

            return Ok(new
            {
                status = 200,
                message = "Bid placed successfully.",
                data = new
                {
                    bid.Id,
                    bid.AuctionId,
                    bid.Amount,
                    bid.PlacedAt,
                    auction.CurrentHighestBid,
                    auction.BidCount
                }
            });
        }


        // GET: api/bid/auction/{auctionId}/bids
        // Returns the bid history for a specific auction, newest first.
        [HttpGet("auction/{auctionId}/bids")]
        public async Task<IActionResult> GetAuctionBidHistory(int auctionId)
        {
            // make sure the auction exists
            var auction = await _context.Auctions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            var bids = await _context.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.PlacedAt)
                .AsNoTracking()
                .Select(b => new BidHistoryDto
                {
                    BidderId = b.BidderId,
                    BidderName = _context.UserProfiles
                        .Where(p => p.UserId == b.BidderId)
                        .Select(p => p.FullName)
                        .AsNoTracking()
                        .FirstOrDefault() ?? "Unknown User",

                    Amount = b.Amount,
                    PlacedAt = b.PlacedAt
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Bids for this Auction Returned Successfully",
                data = bids
            });
        }


        // GET: api/bid/auction/{auctionId}/bids/admin
        // Admin view of an auction's bid history, including bidder email and buyout flag.
        [HttpGet("auction/{auctionId}/bids/admin")]
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> GetAuctionBidHistoryForAdmin(int auctionId)
        {
            var auction = await _context.Auctions
                .Include(a => a.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            var bids = await _context.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.PlacedAt)
                .AsNoTracking()
                .Select(b => new
                {
                    bidId = b.Id,
                    bidderId = b.BidderId,

                    // bidder email from the Identity users table
                    bidderEmail = _context.Users
                        .Where(u => u.Id == b.BidderId)
                        .Select(u => u.Email)
                        .AsNoTracking()
                        .FirstOrDefault() ?? "",

                    // bidder name from their profile
                    bidderName = _context.UserProfiles
                        .Where(p => p.UserId == b.BidderId)
                        .Select(p => p.FullName)
                        .AsNoTracking()
                        .FirstOrDefault() ?? "Unknown User",

                    amount = b.Amount,
                    isBuyout = b.IsBuyout,
                    placedAt = b.PlacedAt,

                    // is this bid still the current highest?
                    isHighest = b.Auction.CurrentHighestBidderId == b.BidderId
                        && b.Auction.CurrentHighestBid == b.Amount
                })
                .ToListAsync();

            return Ok(new
            {
                status = 200,
                message = "Auction bid history retrieved successfully.",
                data = new
                {
                    auctionId = auction.Id,
                    productTitle = auction.Product?.Title,
                    auctionStatus = auction.Status.ToString(),
                    currentHighestBid = auction.CurrentHighestBid,
                    currentHighestBidderId = auction.CurrentHighestBidderId,
                    bidCount = bids.Count,
                    bids
                }
            });
        }


        // POST: api/bid/{auctionId}/buyout
        // Lets a buyer immediately buy the product at its buyout price.
        // This is the "instant win" path — it skips bidding and creates the sale directly.
        [HttpPost("{auctionId}/buyout")]
        [Authorize(Roles = "Buyer")]
        public async Task<IActionResult> Buyout(int auctionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);
            if (auction == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            if (auction.Status != AuctionStatus.Active)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This auction is not active."
                });
            }

            if (!auction.EndTime.HasValue || auction.EndTime.Value <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This auction has ended."
                });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);
            if (product == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            if (product.SellerId == userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    status = 403,
                    message = "Sellers cannot bid on or buy their own auction."
                });
            }

            // product has to be approved and available
            if (product.ApprovalStatus != ProductApprovalStatus.Approved || product.Status != ProductStatus.Available)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved or available for purchase."
                });
            }

            var existingSale = await _context.Sales.AnyAsync(s => s.AuctionId == auctionId);
            if (existingSale)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A sale already exists for this auction."
                });
            }

            if (auction.CurrentHighestBid.HasValue && product.BuyoutPrice > 0 && auction.CurrentHighestBid.Value >= product.BuyoutPrice)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Current highest bid already meets or exceeds the buyout price."
                });
            }

            // Start Transaction
            await using var transaction = await _context.Database.BeginTransactionAsync();


            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Wallet not found."
                });
            }

            // Check if the buyer already has funds locked for this auction
            var reservation = await _context.WalletReservations
                .FirstOrDefaultAsync(r =>
                    r.WalletId == wallet.Id &&
                    r.AuctionId == auctionId &&
                    r.Type == ReservationType.BuyerFunds &&
                    r.Status == ReservationStatus.Reserved);

            // first time participating reserve 25% of the starting price.
            if (reservation == null)
            {
                decimal requiredAmount = product.StartingPrice * 0.25m;
                decimal availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "You do not have enough available balance to participate."
                    });
                }

                wallet.ReservedBalance += requiredAmount;

                reservation = new WalletReservation
                {
                    WalletId = wallet.Id,
                    ProductId = product.Id,
                    AuctionId = auction.Id,
                    Type = ReservationType.BuyerFunds,
                    Amount = requiredAmount,
                    Status = ReservationStatus.Reserved
                };

                _context.WalletReservations.Add(reservation);
            }

            // record the buyout as a bid
            var buyoutBid = new Bid
            {
                AuctionId = auction.Id,
                BidderId = userId,
                Amount = product.BuyoutPrice,
                IsBuyout = true,
                PlacedAt = DateTime.UtcNow
            };

            _context.Bids.Add(buyoutBid);

            // mark the auction as sold and assign the winner
            auction.Status = AuctionStatus.Completed;
            auction.Result = AuctionResult.Sold;
            auction.WinnerId = userId;
            auction.CurrentHighestBid = product.BuyoutPrice;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            // lock the product now so it can't be relisted or bid on while payment is pending
            product.Status = ProductStatus.Unavailable;

            // platform takes 10%, seller gets the rest.
            decimal commissionAmount = Math.Round(product.BuyoutPrice * 0.10m, 2);
            decimal sellerProceeds = product.BuyoutPrice - commissionAmount;

            var now = DateTime.UtcNow;

            // create the sale record — buyer has 3 days to pay before it flips to PaymentOverdue
            var sale = new Sale
            {
                AuctionId = auction.Id,
                ProductId = product.Id,
                BuyerId = userId,
                SellerId = product.SellerId,
                FinalAmount = product.BuyoutPrice,
                CommissionAmount = commissionAmount,
                SellerProceeds = sellerProceeds,
                Status = SaleStatus.AwaitingPayment,
                PaymentDeadline = now.AddDays(3),
                CreatedAt = now
            };

            _context.Sales.Add(sale);

            // release reserved funds for every bidder except the winner
            var reservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == auction.Id &&
                    r.Type == ReservationType.BuyerFunds &&
                    r.Status == ReservationStatus.Reserved)
                .ToListAsync();

            foreach (var buyerReservation in reservations)
            {
                // skip the winner
                if (buyerReservation.WalletId == wallet.Id)
                {
                    continue;
                }

                var otherWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == buyerReservation.WalletId);

                if (otherWallet == null)
                {
                    continue;
                }

                otherWallet.ReservedBalance -= buyerReservation.Amount;
                buyerReservation.Status = ReservationStatus.Released;
            }

            // save the bid, auction, sale, and reservation changes together
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return Ok(new
            {
                status = 200,
                message = "Buyout successful. Please complete payment within 3 days.",
                data = new
                {
                    auction.Id,
                    product.BuyoutPrice,
                    WinnerId = userId,
                    SaleStatus = sale.Status.ToString(),
                    sale.PaymentDeadline
                }
            });
        }

        // Works out the minimum bid increment based on where the current bid sits
        // within the price range (starting → buyout). Range is split into thirds:
        private decimal GetBidIncrement(decimal currentBid, decimal startingPrice, decimal buyoutPrice)
        {
            // total range between starting and buyout price
            var range = buyoutPrice - startingPrice;

            // first third of the range
            if (currentBid < startingPrice + range / 3m)
                return 20m;

            // middle third
            if (currentBid < startingPrice + range * 2m / 3m)
                return 50m;

            // last third
            return 100m;
        }
    }
}