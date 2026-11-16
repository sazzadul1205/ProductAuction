using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;

namespace ProductAuction.API.Data
{
    public class AppDbContext : IdentityDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
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
        public DbSet<DisputeMessage> DisputeMessages { get; set; }
        public DbSet<AuctionView> AuctionViews { get; set; }
        public DbSet<Favorite> Favorites { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Category>()
                .HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AuctionView>()
                .HasIndex(v => new { v.AuctionId, v.IpAddress })
                .IsUnique();

            builder.Entity<AuctionView>()
                .HasOne(v => v.Auction)
                .WithMany()
                .HasForeignKey(v => v.AuctionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Favorite>()
                .HasIndex(f => new { f.UserId, f.AuctionId })
                .IsUnique();

            builder.Entity<Favorite>()
                .HasIndex(f => new { f.UserId, f.CreatedAt });

            builder.Entity<Favorite>()
                .HasOne(f => f.Auction)
                .WithMany()
                .HasForeignKey(f => f.AuctionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}