namespace Orders.Api.Domain;

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Cancelled,
}

/// <summary>
/// Order aggregate. All state changes go through methods that enforce the lifecycle:
/// Pending -> Paid -> Shipped, with cancellation allowed until the order ships.
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
        CustomerEmail = string.Empty;
    }

    public long Id { get; private set; }
    public string CustomerEmail { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every state change.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;
    public decimal Total => _lines.Sum(line => line.LineTotal);

    public static Order Create(string customerEmail, IEnumerable<OrderLine> lines, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerEmail);
        ArgumentNullException.ThrowIfNull(lines);

        var order = new Order
        {
            CustomerEmail = customerEmail.Trim().ToLowerInvariant(),
            Status = OrderStatus.Pending,
            CreatedAt = now,
        };

        foreach (var line in lines)
        {
            order.AddLine(line);
        }

        if (order._lines.Count == 0)
        {
            throw new DomainException("An order must contain at least one line.");
        }

        return order;
    }

    public void MarkPaid(DateTimeOffset now)
    {
        EnsureStatus(OrderStatus.Pending, "pay for");
        Status = OrderStatus.Paid;
        PaidAt = now;
        Version++;
    }

    public void Ship(DateTimeOffset now)
    {
        EnsureStatus(OrderStatus.Paid, "ship");
        Status = OrderStatus.Shipped;
        ShippedAt = now;
        Version++;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Cancelled)
        {
            throw new DomainException($"Cannot cancel an order that is {Describe(Status)}.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        Version++;
    }

    private void AddLine(OrderLine line)
    {
        var existing = _lines.Find(l => l.Sku == line.Sku);
        if (existing is null)
        {
            _lines.Add(line);
            return;
        }

        if (existing.UnitPrice != line.UnitPrice)
        {
            throw new DomainException($"SKU '{line.Sku}' appears with different unit prices.");
        }

        existing.IncreaseQuantity(line.Quantity);
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException($"Cannot {action} an order that is {Describe(Status)}.");
        }
    }

    private static string Describe(OrderStatus status) => status.ToString().ToLowerInvariant();
}
