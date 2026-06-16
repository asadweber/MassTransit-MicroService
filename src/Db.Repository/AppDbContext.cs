using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Db.Repository;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderSagaState> OrderSagaStates => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<OrderSagaState>(e =>
        {
            e.HasKey(s => s.CorrelationId);
            e.Property(s => s.TotalAmount).HasPrecision(18, 2);
            e.Property(s => s.ProductIds)
             .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(int.Parse).ToList())
             .Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (a, i) => HashCode.Combine(a, i)),
                v => v.ToList()));
        });

        modelBuilder.Entity<OrderDetail>(e =>
        {
            e.Property(d => d.UnitPrice).HasPrecision(18, 2);
            e.Property(d => d.Total).HasPrecision(18, 2);

            e.HasOne(d => d.Order)
             .WithMany(o => o.OrderDetails)
             .HasForeignKey(d => d.OrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.Product)
             .WithMany()
             .HasForeignKey(d => d.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
