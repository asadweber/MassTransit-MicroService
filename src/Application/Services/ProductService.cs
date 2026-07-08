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
   
    public async Task<bool> HasSufficientStockAsync(int productId, int qty)
    {
        var product = await uow.Products.GetByIdAsync(productId);
        return product is not null && product.Stock >= qty;
    }
    

    public async Task<bool> ReduceStockQtyAsync(int productId, int qty)
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
}
