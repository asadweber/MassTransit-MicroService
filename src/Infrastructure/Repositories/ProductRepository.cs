using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ProductRepository(AppDbContext context)
    : GenericRepository<Product>(context), IProductRepository
{
    public async Task<bool> HasSufficientStockAsync(int productId, int qty)
    {
        var product = await Context.Products.FindAsync(productId);
        return product is not null && product.Stock >= qty;
    }

    public async Task<bool> ReduceStockQtyAsync(int productId, int qty)
    {
        var product = await Context.Products.FindAsync(productId);
        if (product is null || product.Stock < qty) return false;

        product.Stock -= qty;
        await Context.SaveChangesAsync();
        return true;
    }
}
