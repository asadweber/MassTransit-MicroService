using Application.Dtos;
using Application.Interfaces;
using Application.Messaging.Events;
using AutoMapper;
using Domain;
using Domain.Entities;
using MassTransit;
using MassTransit.MongoDbIntegration;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using static MassTransit.ValidationResultExtensions;

namespace Application.Services;

public class ProductService(IUnitOfWork uow, IPublishEndpoint bus, IMapper mapper) : IProductService
{
   
    public async Task<bool> HasSufficientStockAsync(long productId, long qty)
    {
        var product = await uow.Products.GetByIdAsync(productId);
        return product is not null && product.Stock >= qty;
    }
    

    public async Task<bool> ReduceStockQtyAsync(long productId, long qty)
    {
        var product = await uow.Products.GetByIdAsync(productId);
        if (product is null || product.Stock < qty) return false;

        product.Stock-=qty;

        await uow.BeginTransactionAsync();
        await uow.Products.Update(product);
        await uow.SaveChangesAsync();                                              // 1) order.Id assigned by DB
        await uow.CommitAsync();
        return true;
    }

    public async Task<List<ProductDto>> GetAllAsync()
    {
        var products = await uow.Products.GetAllAsync();
        return mapper.Map<List<ProductDto>>(products);
    }

    public async Task<ProductDto?> GetByIdAsync(long id)
    {
        var product = await uow.Products.GetByIdAsync(id);
        return product is null ? null : mapper.Map<ProductDto>(product);
    }

    public async Task<ProductDto> CreateAsync(ProductDto request)
    {
        var product = mapper.Map<Product>(request);

        await uow.Products.AddAsync(product);
        await uow.SaveChangesAsync();

        return mapper.Map<ProductDto>(product);
    }

    public async Task<bool> UpdateAsync(long id, ProductDto request)
    {
        if (id != request.Id) return false;

        var existing = await uow.Products.GetByIdAsync(id);
        if (existing is null) return false;

        mapper.Map(request, existing);

        await uow.Products.Update(existing);
        await uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var product = await uow.Products.GetByIdAsync(id);
        if (product is null) return false;

        uow.Products.Remove(product);
        await uow.SaveChangesAsync();
        return true;
    }
}
