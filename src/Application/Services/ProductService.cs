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

public class ProductService(IUnitOfWork uow, IPublishEndpoint bus, IMapper mapper)
    : GenericService<Product, ProductDto>(uow.Products, uow, mapper), IProductService
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

        product.Stock -= qty;

        await uow.BeginTransactionAsync();
        await uow.Products.Update(product);
        await uow.SaveChangesAsync();                                              // 1) order.Id assigned by DB
        await uow.CommitAsync();
        return true;
    }

    public override async Task<bool> UpdateAsync(long id, ProductDto request)
    {
        if (id != request.Id) return false;
        return await base.UpdateAsync(id, request);
    }
}
