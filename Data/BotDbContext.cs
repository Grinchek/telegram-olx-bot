using Microsoft.EntityFrameworkCore;
using Data.Entities;

namespace Data;

public class BotDbContext : DbContext
{
    public DbSet<PendingPayment> PendingPayments { get; set; } = default!;
    public DbSet<ConfirmedPayment> ConfirmedPayments { get; set; } = default!;
    public DbSet<PostData> Posts { get; set; } = default!;
    public DbSet<PostCounterEntry> PostCounters { get; set; } = default!;

    public BotDbContext(DbContextOptions<BotDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PendingPayment>()
            .HasOne(p => p.Post)
            .WithMany()
            .HasForeignKey(p => p.PostId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ConfirmedPayment>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.ToTable("confirmed_payments");

            entity.HasOne(p => p.Post)
                  .WithMany()
                  .HasForeignKey(nameof(ConfirmedPayment.PostId))
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PostData>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.ToTable("posts");
        });

        modelBuilder.Entity<PostCounterEntry>(entity =>
        {
            entity.HasKey(p => p.Date);
            entity.ToTable("post_counters");
        });

        base.OnModelCreating(modelBuilder);
    }
}
