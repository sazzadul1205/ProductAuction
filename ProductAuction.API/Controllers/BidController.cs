using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Bid;
using ProductAuction.API.Models;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BidController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BidController> _logger;

        public BidController(AppDbContext context, ILogger<BidController> logger)
        {
            _context = context;
            _logger = logger;
        }


        // POST: api/bid
        // Places a bid on an active auction.
        [HttpPost]
        [Authorize(Roles = "Buyer")]
        public async Task<IActionResult> PlaceBid(PlaceBidDto dto)
        {
            // get the logged-in user's id
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                // _logger.LogWarning("PlaceBid: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // _logger.LogInformation("PlaceBid called by user {UserId} for auction #{AuctionId}, amount={Amount}.",
            //     userId, dto.AuctionId, dto.Amount);

            // find the auction
            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == dto.AuctionId);

            if (auction == null)
            {
                // _logger.LogWarning("PlaceBid: auction #{AuctionId} not found.", dto.AuctionId);

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);

            if (product == null)
            {
                // _logger.LogWarning("PlaceBid: product #{ProductId} for auction #{AuctionId} not found.",
                //     auction.ProductId, auction.Id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // only active auctions accept bids
            if (auction.Status != "Active")
            {
                // _logger.LogWarning("PlaceBid: auction #{AuctionId} is not Active (status={Status}).",
                //     auction.Id, auction.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "Bids can only be placed on active auctions."
                });
            }

            if (auction.EndTime == null || auction.EndTime <= DateTime.UtcNow)
            {
                // _logger.LogWarning("PlaceBid: auction #{AuctionId} has already ended.", auction.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "This auction has ended."
                });
            }

            // product has to be approved and available
            if (product.ApprovalStatus != "Approved" || product.Status != "Available")
            {
                // _logger.LogWarning("PlaceBid: product #{ProductId} not approved/available (approval={Approval}, status={Status}).",
                //     product.Id, product.ApprovalStatus, product.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved or available for bidding."
                });
            }

            // sellers can't bid on their own stuff
            if (auction.SellerId == userId)
            {
                // _logger.LogWarning("PlaceBid: user {UserId} tried to bid on their own auction #{AuctionId}.",
                //     userId, auction.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "You cannot bid on your own product."
                });
            }

            // bids at or above the buyout price should use the buyout endpoint instead
            if (product.BuyoutPrice > 0 && dto.Amount >= product.BuyoutPrice)
            {
                // _logger.LogWarning("PlaceBid: amount {Amount} >= buyout {Buyout} for auction #{AuctionId}.",
                //     dto.Amount, product.BuyoutPrice, auction.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Your bid must be below the buyout price. Use the buyout endpoint."
                });
            }

            // first bid has to meet the starting price
            if (dto.Amount < product.StartingPrice)
            {
                // _logger.LogWarning("PlaceBid: amount {Amount} below starting price {Starting} for auction #{AuctionId}.",
                //     dto.Amount, product.StartingPrice, auction.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = $"Minimum bid is {product.StartingPrice:F2}."
                });
            }

            // subsequent bids need to clear the minimum increment
            if (auction.CurrentHighestBid.HasValue)
            {
                var highestBid = auction.CurrentHighestBid.Value;

                var increment = GetBidIncrement(
                    highestBid,
                    product.StartingPrice,
                    product.BuyoutPrice);

                var minimumBid = highestBid + increment;

                if (dto.Amount < minimumBid)
                {
                    // _logger.LogWarning("PlaceBid: amount {Amount} below minimum {Minimum} for auction #{AuctionId}.",
                    //     dto.Amount, minimumBid, auction.Id);

                    return BadRequest(new
                    {
                        status = 400,
                        message = $"Minimum bid is {minimumBid:F2}."
                    });
                }
            }

            // find the buyer's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("PlaceBid: wallet for user {UserId} not found.", userId);

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
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved");

            // reserve 25% of the starting price, but only once per auction
            if (reservation == null)
            {
                var requiredAmount = product.StartingPrice * 0.25m;
                var availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    // _logger.LogWarning("PlaceBid: user {UserId} has insufficient available balance ({Available}) for reservation {Required}.",
                    //     userId, availableBalance, requiredAmount);

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
                    Type = "BuyerFunds",
                    Amount = requiredAmount,
                    Status = "Reserved"
                };

                _context.WalletReservations.Add(reservation);

                // _logger.LogInformation("PlaceBid: reserved {Amount} for user {UserId} on auction #{AuctionId}.",
                //     requiredAmount, userId, auction.Id);
            }

            // create the bid
            var bid = new Bid
            {
                AuctionId = auction.Id,
                BidderId = userId,
                Amount = dto.Amount,
                PlacedAt = DateTime.UtcNow
            };

            _context.Bids.Add(bid);

            // bump the auction's highest bid info
            auction.CurrentHighestBid = dto.Amount;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            await _context.SaveChangesAsync();

            // System.Diagnostics.Debug.Assert(auction.CurrentHighestBid == dto.Amount);
            // System.Diagnostics.Debug.Assert(auction.CurrentHighestBidderId == userId);

            // _logger.LogInformation("PlaceBid: bid #{BidId} of {Amount} placed by user {UserId} on auction #{AuctionId}.",
            //     bid.Id, bid.Amount, userId, auction.Id);

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
            // _logger.LogInformation("GetAuctionBidHistory called for auction #{AuctionId}.", auctionId);

            // make sure the auction exists
            var auction = await _context.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                // _logger.LogWarning("GetAuctionBidHistory: auction #{AuctionId} not found.", auctionId);

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            var bids = await _context.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.PlacedAt)
                .Select(b => new BidHistoryDto
                {
                    BidderId = b.BidderId,

                    // pull the bidder's name from their profile
                    BidderName = _context.UserProfiles
                        .Where(p => p.UserId == b.BidderId)
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown User",

                    Amount = b.Amount,
                    PlacedAt = b.PlacedAt
                })
                .ToListAsync();

            // _logger.LogInformation("GetAuctionBidHistory returned {Count} bid(s) for auction #{AuctionId}.",
            //     bids.Count, auctionId);

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
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAuctionBidHistoryForAdmin(int auctionId)
        {
            // _logger.LogInformation("GetAuctionBidHistoryForAdmin called for auction #{AuctionId}.", auctionId);

            var auction = await _context.Auctions
                .Include(a => a.Product)
                .FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                // _logger.LogWarning("GetAuctionBidHistoryForAdmin: auction #{AuctionId} not found.", auctionId);

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            var bids = await _context.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.PlacedAt)
                .Select(b => new
                {
                    bidId = b.Id,
                    bidderId = b.BidderId,

                    // bidder email from the Identity users table
                    bidderEmail = _context.Users
                        .Where(u => u.Id == b.BidderId)
                        .Select(u => u.Email)
                        .FirstOrDefault() ?? "",

                    // bidder name from their profile
                    bidderName = _context.UserProfiles
                        .Where(p => p.UserId == b.BidderId)
                        .Select(p => p.FullName)
                        .FirstOrDefault() ?? "Unknown User",

                    amount = b.Amount,
                    isBuyout = b.IsBuyout,
                    placedAt = b.PlacedAt,

                    // is this bid still the current highest?
                    isHighest = b.Auction.CurrentHighestBidderId == b.BidderId
                        && b.Auction.CurrentHighestBid == b.Amount
                })
                .ToListAsync();

            // _logger.LogInformation("GetAuctionBidHistoryForAdmin returned {Count} bid(s) for auction #{AuctionId}.",
            //     bids.Count, auctionId);

            return Ok(new
            {
                status = 200,
                message = "Auction bid history retrieved successfully.",
                data = new
                {
                    auctionId = auction.Id,
                    productTitle = auction.Product?.Title,
                    auctionStatus = auction.Status,
                    currentHighestBid = auction.CurrentHighestBid,
                    currentHighestBidderId = auction.CurrentHighestBidderId,
                    bidCount = bids.Count,
                    bids
                }
            });
        }


        // POST: api/bid/{auctionId}/buyout
        // Lets a buyer immediately buy the product at its buyout price.
        [HttpPost("{auctionId}/buyout")]
        [Authorize(Roles = "Buyer")]
        public async Task<IActionResult> Buyout(int auctionId)
        {
            // get the logged-in user's id
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                // _logger.LogWarning("Buyout: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "Please log in first."
                });
            }

            // _logger.LogInformation("Buyout called by user {UserId} for auction #{AuctionId}.", userId, auctionId);

            // find the auction
            var auction = await _context.Auctions
                .FirstOrDefaultAsync(a => a.Id == auctionId);

            if (auction == null)
            {
                // _logger.LogWarning("Buyout: auction #{AuctionId} not found.", auctionId);

                return NotFound(new
                {
                    status = 404,
                    message = "Auction not found."
                });
            }

            // auction has to be active and not past its end time
            if (auction.Status != "Active")
            {
                // _logger.LogWarning("Buyout: auction #{AuctionId} is not Active (status={Status}).",
                //     auction.Id, auction.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "This auction is not active."
                });
            }

            if (!auction.EndTime.HasValue || auction.EndTime.Value <= DateTime.UtcNow)
            {
                // _logger.LogWarning("Buyout: auction #{AuctionId} has already ended.", auction.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "This auction has ended."
                });
            }

            // find the product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == auction.ProductId);

            if (product == null)
            {
                // _logger.LogWarning("Buyout: product #{ProductId} for auction #{AuctionId} not found.",
                //     auction.ProductId, auction.Id);

                return NotFound(new
                {
                    status = 404,
                    message = "Product not found."
                });
            }

            // sellers can't buy their own product
            if (auction.SellerId == userId)
            {
                // _logger.LogWarning("Buyout: user {UserId} tried to buy out their own auction #{AuctionId}.",
                //     userId, auction.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "You cannot buy out your own product."
                });
            }

            // product has to be approved and available
            if (product.ApprovalStatus != "Approved" || product.Status != "Available")
            {
                // _logger.LogWarning("Buyout: product #{ProductId} not approved/available (approval={Approval}, status={Status}).",
                //     product.Id, product.ApprovalStatus, product.Status);

                return BadRequest(new
                {
                    status = 400,
                    message = "This product is not approved or available for purchase."
                });
            }

            // and it actually needs a buyout price
            if (product.BuyoutPrice <= 0)
            {
                // _logger.LogWarning("Buyout: product #{ProductId} has no valid buyout price.", product.Id);

                return BadRequest(new
                {
                    status = 400,
                    message = "This product has no valid buyout price."
                });
            }

            // don't let the same auction be sold twice
            var existingSale = await _context.Sales.AnyAsync(s => s.AuctionId == auctionId);

            if (existingSale)
            {
                // _logger.LogWarning("Buyout: sale already exists for auction #{AuctionId}.", auctionId);

                return BadRequest(new
                {
                    status = 400,
                    message = "A sale already exists for this auction."
                });
            }

            // all the wallet + sale changes below need to succeed together
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // find the buyer's wallet
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
            {
                // _logger.LogWarning("Buyout: wallet for user {UserId} not found.", userId);

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
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved");

            // first time participating? reserve 25% of the starting price
            if (reservation == null)
            {
                decimal requiredAmount = product.StartingPrice * 0.25m;
                decimal availableBalance = wallet.Balance - wallet.ReservedBalance;

                if (availableBalance < requiredAmount)
                {
                    // _logger.LogWarning("Buyout: user {UserId} has insufficient available balance ({Available}) for reservation {Required}.",
                    //     userId, availableBalance, requiredAmount);

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
                    Type = "BuyerFunds",
                    Amount = requiredAmount,
                    Status = "Reserved"
                };

                _context.WalletReservations.Add(reservation);

                // _logger.LogInformation("Buyout: reserved {Amount} for user {UserId} on auction #{AuctionId}.",
                //     requiredAmount, userId, auction.Id);
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
            auction.Status = "Completed";
            auction.Result = "Sold";
            auction.WinnerId = userId;
            auction.CurrentHighestBid = product.BuyoutPrice;
            auction.CurrentHighestBidderId = userId;
            auction.BidCount++;

            // Mark product as unavailable after sale
            product.Status = "Unavailable";

            // platform takes 10%, seller gets the rest
            decimal commissionAmount = Math.Round(product.BuyoutPrice * 0.10m, 2);
            decimal sellerProceeds = product.BuyoutPrice - commissionAmount;

            var now = DateTime.UtcNow;

            // create the sale record
            var sale = new Sale
            {
                AuctionId = auction.Id,
                ProductId = product.Id,
                BuyerId = userId,
                SellerId = product.SellerId,
                FinalAmount = product.BuyoutPrice,
                CommissionAmount = commissionAmount,
                SellerProceeds = sellerProceeds,
                Status = "AwaitingPayment",
                PaymentDeadline = now.AddDays(3),
                CreatedAt = now
            };

            _context.Sales.Add(sale);

            // release reserved funds for every bidder except the winner
            var reservations = await _context.WalletReservations
                .Where(r =>
                    r.AuctionId == auction.Id &&
                    r.Type == "BuyerFunds" &&
                    r.Status == "Reserved")
                .ToListAsync();

            foreach (var buyerReservation in reservations)
            {
                // skip the winner — their funds stay locked until payment
                if (buyerReservation.WalletId == wallet.Id)
                {
                    continue;
                }

                var otherWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == buyerReservation.WalletId);

                if (otherWallet == null)
                {
                    // _logger.LogWarning("Buyout: wallet #{WalletId} for losing bidder not found; skipping release.",
                    //     buyerReservation.WalletId);
                    continue;
                }

                otherWallet.ReservedBalance -= buyerReservation.Amount;
                buyerReservation.Status = "Released";
            }

            // save the bid, auction, sale, and reservation changes together
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            // System.Diagnostics.Debug.Assert(auction.Status == "Completed" && auction.Result == "Sold");
            // System.Diagnostics.Debug.Assert(auction.WinnerId == userId);
            // System.Diagnostics.Debug.Assert(product.Status == "Unavailable");

            // _logger.LogInformation("Buyout: auction #{AuctionId} sold to user {UserId} for {Amount}.",
            //     auction.Id, userId, product.BuyoutPrice);

            return Ok(new
            {
                status = 200,
                message = "Buyout successful. Please complete payment within 3 days.",
                data = new
                {
                    auction.Id,
                    product.BuyoutPrice,
                    WinnerId = userId,
                    sale.Status,
                    sale.PaymentDeadline
                }
            });
        }


        // Works out the minimum bid increment based on where the current bid sits
        // within the price range (starting → buyout). Range is split into thirds:
        // first third = 20, middle third = 50, last third = 100.
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