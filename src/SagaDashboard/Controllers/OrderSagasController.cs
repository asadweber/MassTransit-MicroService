using Application.Dtos;
using Application.Interfaces;
using Application.Messaging.Events;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SagaDashboard.Controllers;

public class OrderSagasController : Controller
{
    private readonly AppDbContext _db;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IPublishEndpoint _bus;

    public OrderSagasController(
        AppDbContext db,
        IOrderService orderService,
        IProductService productService,
        IPublishEndpoint bus)
    {
        _db = db;
        _orderService = orderService;
        _productService = productService;
        _bus = bus;
    }

    public async Task<IActionResult> Index(int? orderId, string? state, CancellationToken ct)
    {
        var query = _db.OrderSagaStates.Include(s => s.OrderDetails).AsQueryable();

        if (orderId.HasValue)
        {
            query = query.Where(s => s.OrderId == orderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(s => s.CurrentState == state);
        }

        var results = await query
            .OrderByDescending(s => s.OrderDate)
            .Take(200)
            .ToListAsync(ct);

        ViewBag.OrderId = orderId;
        ViewBag.State = state;

        return View(results);
    }

    // Reachable for a Failed saga (restart from scratch) or a CheckingInventory saga stuck
    // retrying (edit line items in place — the next scheduled retry re-checks saga.Order).
    public async Task<IActionResult> Edit(Guid correlationId, CancellationToken ct)
    {
        var saga = await _db.OrderSagaStates.Include(s => s.OrderDetails)
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
        if (saga is null || (saga.CurrentState != "Failed" && saga.CurrentState != "CheckingInventory"))
            return NotFound();

        ViewBag.Products = await _productService.GetAllAsync();
        ViewBag.SagaState = saga.CurrentState;
        return View(ToOrderDto(saga));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restart(Guid correlationId, OrderDto request, CancellationToken ct)
    {
        var saga = await _db.OrderSagaStates.FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
        if (saga is null || saga.CurrentState != "Failed") return NotFound();
        if (saga.OrderId != request.Id) return NotFound();

        var orderId = saga.OrderId;
        var updated = await _orderService.UpdateAsync(orderId, request);
        if (!updated) return NotFound();

        // Drop the terminated saga instance and re-publish OrderCreated to start a fresh saga
        // with the corrected order data.
        _db.OrderSagaStates.Remove(saga);
        await _db.SaveChangesAsync(ct);

        var order = await _orderService.GetByIdAsync(orderId);
        await _bus.Publish(new OrderCreated { Order = order! }, ct);

        return RedirectToAction(nameof(Index));
    }

    // Live edit for a saga stuck retrying CheckingInventory: updates the SQL order row and the
    // saga's own Order snapshot in place, without touching saga state or the pending retry
    // schedule — the next InventoryRetry fire re-checks stock against the corrected line items.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInFlight(Guid correlationId, OrderDto request, CancellationToken ct)
    {
        var saga = await _db.OrderSagaStates.Include(s => s.OrderDetails)
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
        if (saga is null || saga.CurrentState != "CheckingInventory") return NotFound();
        if (saga.OrderId != request.Id) return NotFound();

        var orderId = saga.OrderId;
        var updated = await _orderService.UpdateAsync(orderId, request);
        if (!updated) return NotFound();

        var order = await _orderService.GetByIdAsync(orderId);

        saga.CustomerName = order!.CustomerName;
        saga.OrderDate = order.OrderDate;
        saga.TotalAmount = order.TotalAmount;
        saga.Status = order.Status;
        saga.OrderDetails = order.OrderDetails.Select(d => new SagaOrderDetail
        {
            OrderSagaStateCorrelationId = saga.CorrelationId,
            ProductId = d.ProductId,
            OrderQty = d.OrderQty,
            UnitPrice = d.UnitPrice,
            Total = d.Total,
        }).ToList();

        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    private static OrderDto ToOrderDto(OrderSagaState saga) => new()
    {
        Id = saga.OrderId,
        CustomerName = saga.CustomerName,
        OrderDate = saga.OrderDate,
        TotalAmount = saga.TotalAmount,
        Status = saga.Status,
        OrderDetails = saga.OrderDetails.Select(d => new OrderDetailDto
        {
            Id = d.Id,
            OrderId = saga.OrderId,
            ProductId = d.ProductId,
            OrderQty = d.OrderQty,
            UnitPrice = d.UnitPrice,
            Total = d.Total,
        }).ToList(),
    };
}
