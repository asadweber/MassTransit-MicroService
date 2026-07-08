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

    // Reachable for a Failed saga (restart from scratch) or a CheckingInventory saga stuck
    // retrying (edit line items in place — the next scheduled retry re-checks saga.Order).
    public async Task<IActionResult> Edit(Guid correlationId, CancellationToken ct)
    {
        var saga = await _sagas.Find(s => s.CorrelationId == correlationId).FirstOrDefaultAsync(ct);
        if (saga is null || (saga.CurrentState != "Failed" && saga.CurrentState != "CheckingInventory"))
            return NotFound();

        ViewBag.Products = await _productService.GetAllAsync();
        ViewBag.SagaState = saga.CurrentState;
        return View(saga.Order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restart(Guid correlationId, OrderDto request, CancellationToken ct)
    {
        var saga = await _sagas.Find(s => s.CorrelationId == correlationId).FirstOrDefaultAsync(ct);
        if (saga is null || saga.CurrentState != "Failed") return NotFound();
        if (saga.Order?.Id != request.Id) return NotFound();

        var orderId = saga.Order.Id;
        var updated = await _orderService.UpdateAsync(orderId, request);
        if (!updated) return NotFound();

        // Drop the terminated saga instance and re-publish OrderCreated to start a fresh saga
        // with the corrected order data.
        await _sagas.DeleteOneAsync(s => s.CorrelationId == correlationId, ct);

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
        var saga = await _sagas.Find(s => s.CorrelationId == correlationId).FirstOrDefaultAsync(ct);
        if (saga is null || saga.CurrentState != "CheckingInventory") return NotFound();
        if (saga.Order?.Id != request.Id) return NotFound();

        var orderId = saga.Order.Id;
        var updated = await _orderService.UpdateAsync(orderId, request);
        if (!updated) return NotFound();

        var order = await _orderService.GetByIdAsync(orderId);
        await _sagas.UpdateOneAsync(
            s => s.CorrelationId == correlationId,
            Builders<OrderSagaState>.Update.Set(s => s.Order, order),
            cancellationToken: ct);

        return RedirectToAction(nameof(Index));
    }
}
