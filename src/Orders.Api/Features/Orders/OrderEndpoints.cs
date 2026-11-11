using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Features.Orders;

public static class OrderEndpoints
{
    private const string GetOrderRoute = "GetOrder";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", CreateOrder);
        group.MapGet("/", ListOrders);
        group.MapGet("/{id:long}", GetOrder).WithName(GetOrderRoute);
        group.MapPost("/{id:long}/pay", (long id, OrdersDbContext db, TimeProvider clock, CancellationToken ct) =>
            ChangeState(id, db, order => order.MarkPaid(clock.GetUtcNow()), ct));
        group.MapPost("/{id:long}/ship", (long id, OrdersDbContext db, TimeProvider clock, CancellationToken ct) =>
            ChangeState(id, db, order => order.Ship(clock.GetUtcNow()), ct));
        group.MapPost("/{id:long}/cancel", (long id, OrdersDbContext db, TimeProvider clock, CancellationToken ct) =>
            ChangeState(id, db, order => order.Cancel(clock.GetUtcNow()), ct));

        return app;
    }

    private static async Task<Results<CreatedAtRoute<OrderResponse>, ValidationProblem>> CreateOrder(
        CreateOrderRequest request, OrdersDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var lines = request.Lines.Select(line => new OrderLine(line.Sku, line.Quantity, line.UnitPrice));
        var order = Order.Create(request.CustomerEmail, lines, clock.GetUtcNow());

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        return TypedResults.CreatedAtRoute(OrderResponse.From(order), GetOrderRoute, new { id = order.Id });
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrder(long id, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(OrderResponse.From(order));
    }

    private static async Task<Results<Ok<PagedResponse<OrderResponse>>, ValidationProblem>> ListOrders(
        OrdersDbContext db,
        CancellationToken ct,
        OrderStatus? status = null,
        string? customerEmail = null,
        int limit = 20,
        int offset = 0)
    {
        if (limit is < 1 or > 100 || offset < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["paging"] = ["limit must be between 1 and 100 and offset must be zero or greater."],
            });
        }

        var query = db.Orders.AsNoTracking();
        if (status is { } statusFilter)
        {
            query = query.Where(o => o.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            var email = customerEmail.Trim().ToLowerInvariant();
            query = query.Where(o => o.CustomerEmail == email);
        }

        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.Id).Skip(offset).Take(limit).ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<OrderResponse>([.. orders.Select(OrderResponse.From)], total, limit, offset));
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> ChangeState(
        long id, OrdersDbContext db, Action<Order> change, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        change(order);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(OrderResponse.From(order));
    }
}
