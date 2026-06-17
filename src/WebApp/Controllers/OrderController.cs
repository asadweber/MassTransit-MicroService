using AutoMapper;
using Contracts.Dto;
using Contracts.Messages.Events;
using Db.Repository;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Swagger;

namespace WebApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController(AppDbContext db, IPublishEndpoint bus, IMapper mapper) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orders = await db.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .ToListAsync();

        return Ok(mapper.Map<List<OrderDto>>(orders));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await db.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        return order is null ? NotFound() : Ok(mapper.Map<OrderDto>(order));
    }

    [HttpPost]
    [SwaggerRequestExample(typeof(OrderDto), typeof(OrderDtoExample))]
    public async Task<IActionResult> Create(OrderDto request)
    {
        var order = mapper.Map<Order>(request);
        order.OrderDate = DateTime.UtcNow;

        foreach (var detail in order.OrderDetails)
            detail.Total = detail.OrderQty * detail.UnitPrice;

        order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

        await using var tx = await db.Database.BeginTransactionAsync();

        db.Orders.Add(order);                                               // stage Order

        await bus.Publish(new OrderCreated { Order = mapper.Map<OrderDto>(order) }); // stage OutboxMessage

        await db.SaveChangesAsync();                                        // flush both in ONE call

        await tx.CommitAsync();                                             // commit atomically

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, mapper.Map<OrderDto>(order));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, OrderDto request)
    {
        if (id != request.Id) return BadRequest();

        var existing = await db.Orders
            .Include(o => o.OrderDetails)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (existing is null) return NotFound();

        mapper.Map(request, existing);

        db.OrderDetails.RemoveRange(existing.OrderDetails);

        existing.OrderDetails = mapper.Map<List<OrderDetail>>(request.OrderDetails);
        foreach (var detail in existing.OrderDetails)
        {
            detail.OrderId = id;
            detail.Total = detail.OrderQty * detail.UnitPrice;
        }

        existing.TotalAmount = existing.OrderDetails.Sum(d => d.Total);

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        db.Orders.Remove(order);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
