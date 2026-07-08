using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using OrderSaga.Saga;

namespace SagaDashboard.Controllers;

public class OrderSagasController : Controller
{
    private readonly IMongoCollection<OrderSagaState> _sagas;

    public OrderSagasController(IMongoCollection<OrderSagaState> sagas)
    {
        _sagas = sagas;
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
}
