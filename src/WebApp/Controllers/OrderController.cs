using Contracts;
using Db.Repository;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController(AppDbContext db, IPublishEndpoint bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orders = await db.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .ToListAsync();
        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await db.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Order order)
    {
        order.OrderDate = DateTime.UtcNow;

        foreach (var detail in order.OrderDetails)
        {
            detail.Total = detail.OrderQty * detail.UnitPrice;
        }

        order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await bus.Publish(new OrderCreated(
            order.Id,
            order.CustomerName,
            order.OrderDate,
            order.TotalAmount,
            order.Status));

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Order order)
    {
        if (id != order.Id) return BadRequest();

        var existing = await db.Orders
            .Include(o => o.OrderDetails)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (existing is null) return NotFound();

        existing.CustomerName = order.CustomerName;
        existing.Status = order.Status;

        db.OrderDetails.RemoveRange(existing.OrderDetails);

        foreach (var detail in order.OrderDetails)
        {
            detail.OrderId = id;
            detail.Total = detail.OrderQty * detail.UnitPrice;
        }

        existing.OrderDetails = order.OrderDetails;
        existing.TotalAmount = order.OrderDetails.Sum(d => d.Total);

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
