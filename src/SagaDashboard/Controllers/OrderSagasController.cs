using Application.Dtos;
using Application.Interfaces;
using Application.Messaging.Events;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using OrderSaga.Saga;

namespace SagaDashboard.Controllers;

public class OrderSagasController : Controller
{
    private readonly IMongoCollection<OrderSagaState> _sagas;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IPublishEndpoint _bus;

    public OrderSagasController(
        IMongoCollection<OrderSagaState> sagas,
        IOrderService orderService,
        IProductService productService,
        IPublishEndpoint bus)
    {
        _sagas = sagas;
        _orderService = orderService;
        _productService = productService;
        _bus = bus;
    }

    public async Task<IActionResult> Index(int? orderId, string? state, CancellationToken ct)
    {
        var filterBuilder = Builders<OrderSagaState>.Filter;
        var filter = filterBuilder.Empty;

        if (orderId.HasValue)
        {
            filter &= filterBuilder.Eq(s => s.Order.Id, orderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            filter &= filterBuilder.Eq(s => s.CurrentState, state);
        }

        var results = await _sagas.Find(filter)
            .SortByDescending(s => s.Order.OrderDate)
            .Limit(200)
            .ToListAsync(ct);

        ViewBag.OrderId = orderId;
        ViewBag.State = state;

        return View(results);
    }

    // Only reachable for a Failed saga — editing a still-in-flight saga would race the state machine.
    public async Task<IActionResult> Edit(Guid correlationId, CancellationToken ct)
    {
        var saga = await _sagas.Find(s => s.CorrelationId == correlationId).FirstOrDefaultAsync(ct);
        if (saga is null || saga.CurrentState != "Failed") return NotFound();

        ViewBag.Products = await _productService.GetAllAsync();
        return View(saga.Order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restart(Guid correlationId, OrderDto request, CancellationToken ct)
    {
        var saga = await _sagas.Find(s => s.CorrelationId == correlationId).FirstOrDefaultAsync(ct);
        if (saga is null || saga.CurrentState != "Failed") return NotFound();

        var updated = await _orderService.UpdateAsync(request.Id, request);
        if (!updated) return NotFound();

        // Drop the terminated saga instance and re-publish OrderCreated to start a fresh saga
        // with the corrected order data.
        await _sagas.DeleteOneAsync(s => s.CorrelationId == correlationId, ct);

        var order = await _orderService.GetByIdAsync(request.Id);
        await _bus.Publish(new OrderCreated { Order = order! }, ct);

        return RedirectToAction(nameof(Index));
    }
}
