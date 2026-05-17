using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Data
{
    public class SmapiDbContext : DbContext
    {
        public SmapiDbContext(DbContextOptions<SmapiDbContext> options) : base(options)
        {
        }

        public DbSet<FacebookPage> FacebookPages { get; set; }
        public DbSet<FacebookPostUrl> FacebookPostUrls { get; set; }
        public DbSet<FacebookReelUploadJob> FacebookReelUploadJobs { get; set; }
        public DbSet<S3StorageSetting> S3StorageSettings { get; set; }
        public DbSet<ApifySetting> ApifySettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<FacebookPage>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.PageId }).IsUnique();
            });

            modelBuilder.Entity<FacebookPostUrl>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.PageId, e.Platform, e.PermalinkUrl }).IsUnique();
                entity.HasIndex(e => new { e.UserId, e.PageId, e.Platform });
                entity.HasIndex(e => e.PostId);
            });

            modelBuilder.Entity<FacebookReelUploadJob>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.CreatedAt });
                entity.HasIndex(e => e.Status);
                entity.HasOne(e => e.FacebookPostUrl)
                    .WithMany()
                    .HasForeignKey(e => e.FacebookPostUrlId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<S3StorageSetting>(entity =>
            {
                entity.HasIndex(e => e.UserId).IsUnique();
            });

            modelBuilder.Entity<ApifySetting>(entity =>
            {
                entity.HasIndex(e => e.UserId).IsUnique();
            });
        }
    }
}
