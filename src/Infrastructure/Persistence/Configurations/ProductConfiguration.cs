using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Price).HasPrecision(18, 2);

        builder.HasData(
            new Product { Id = 1, Name = "Laptop", Price = 999.99m, Stock = 50 },
            new Product { Id = 2, Name = "Wireless Mouse", Price = 29.99m, Stock = 200 },
            new Product { Id = 3, Name = "USB-C Hub", Price = 49.99m, Stock = 150 },
            new Product { Id = 4, Name = "Mechanical Keyboard", Price = 89.99m, Stock = 75 },
            new Product { Id = 5, Name = "Monitor 27\"", Price = 349.99m, Stock = 30 }
        );
    }
}
