using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Db.Repository;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost;Database=OrderDB;User Id=sa;Password=Asdf1234;Connect Timeout=30;Min Pool Size=5;Max Pool Size=100;TrustServerCertificate=True;");

        return new AppDbContext(optionsBuilder.Options);
    }
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderSagaState> OrderSagaStates => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.HasData(
                new Product { Id = 1, Name = "Laptop",        Price = 999.99m,  Stock = 50  },
                new Product { Id = 2, Name = "Wireless Mouse", Price = 29.99m,  Stock = 200 },
                new Product { Id = 3, Name = "USB-C Hub",      Price = 49.99m,  Stock = 150 },
                new Product { Id = 4, Name = "Mechanical Keyboard", Price = 89.99m, Stock = 75 },
                new Product { Id = 5, Name = "Monitor 27\"",   Price = 349.99m, Stock = 30  }
            );
        });

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

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
