using Application.Dtos;
using Application.Interfaces;
using Application.Messaging.Events;
using AutoMapper;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Swagger;

namespace WebApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController(IOrderService orderService, IPublishEndpoint bus, IMapper mapper) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await orderService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await orderService.GetByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    [SwaggerRequestExample(typeof(OrderDto), typeof(OrderDtoExample))]
    public async Task<IActionResult> Create(OrderDto request)
    {
        var result = await orderService.CreateAsync(request);        
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, OrderDto request)
    {
        var updated = await orderService.UpdateAsync(id, request);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await orderService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
