using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models;

namespace ProductAuction.API.Data
{
    public class AppDbContext : IdentityDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
        {
        }

        public DbSet<UserProfile> UserProfiles { get; set; }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }

        public DbSet<Auction> Auctions { get; set; }

        public DbSet<Bid> Bids { get; set; }
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<DepositRequest> DepositRequests { get; set; }

        public DbSet<WalletReservation> WalletReservations { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<DeliveryRecord> DeliveryRecords { get; set; }
        public DbSet<Dispute> Disputes { get; set; }
        public DbSet<Penalty> Penalties { get; set; }

    }
}
