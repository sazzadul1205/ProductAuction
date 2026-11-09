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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Auction
            modelBuilder.Entity<Auction>()
                .Property(a => a.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<Auction>()
                .Property(a => a.Result)
                .HasMaxLength(30);

            // Product
            modelBuilder.Entity<Product>()
                .Property(p => p.ApprovalStatus)
                .HasMaxLength(30);

            modelBuilder.Entity<Product>()
                .Property(p => p.Status)
                .HasMaxLength(30);

            // Sale
            modelBuilder.Entity<Sale>()
                .Property(s => s.Status)
                .HasMaxLength(30);

            // DeliveryRecord
            modelBuilder.Entity<DeliveryRecord>()
                .Property(d => d.Status)
                .HasMaxLength(30);

            // Dispute
            modelBuilder.Entity<Dispute>()
                .Property(d => d.Status)
                .HasMaxLength(30);

            // Penalty
            modelBuilder.Entity<Penalty>()
                .Property(p => p.Status)
                .HasMaxLength(30);

            // WalletReservation
            modelBuilder.Entity<WalletReservation>()
                .Property(r => r.Type)
                .HasMaxLength(30);

            modelBuilder.Entity<WalletReservation>()
                .Property(r => r.Status)
                .HasMaxLength(30);

            // WalletTransaction
            modelBuilder.Entity<WalletTransaction>()
                .Property(t => t.Type)
                .HasMaxLength(30);

            // UserProfile
            modelBuilder.Entity<UserProfile>()
                .Property(p => p.VerificationStatus)
                .HasMaxLength(30);

            // DepositRequest
            modelBuilder.Entity<DepositRequest>()
                .Property(d => d.Status)
                .HasMaxLength(30);
        }
    }
}