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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<FacebookPage>(entity =>
            {
                entity.HasIndex(e => e.PageId).IsUnique();
            });
        }
    }
}
