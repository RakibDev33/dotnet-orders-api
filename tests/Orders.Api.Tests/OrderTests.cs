using Orders.Api.Domain;

namespace Orders.Api.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() => Order.Create("buyer@example.com", [new OrderLine("sku-1", 2, 10m)], Now);

    [Fact]
    public void Create_normalizes_input_merges_duplicate_skus_and_computes_total()
    {
        var order = Order.Create(
            " Buyer@Example.com ",
            [new OrderLine("abc", 1, 5m), new OrderLine("ABC", 2, 5m), new OrderLine("xyz", 1, 2.5m)],
            Now);

        Assert.Equal("buyer@example.com", order.CustomerEmail);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(3, order.Lines.Single(line => line.Sku == "ABC").Quantity);
        Assert.Equal(17.5m, order.Total);
    }

    [Fact]
    public void Create_rejects_conflicting_prices_for_the_same_sku()
    {
        Assert.Throws<DomainException>(() =>
            Order.Create("buyer@example.com", [new OrderLine("abc", 1, 5m), new OrderLine("abc", 1, 6m)], Now));
    }

    [Fact]
    public void Create_requires_at_least_one_line()
    {
        Assert.Throws<DomainException>(() => Order.Create("buyer@example.com", [], Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(OrderLine.MaxQuantity + 1)]
    public void OrderLine_rejects_quantities_out_of_range(int quantity)
    {
        Assert.Throws<DomainException>(() => new OrderLine("abc", quantity, 1m));
    }

    [Fact]
    public void Pay_then_ship_records_timestamps_and_bumps_the_version()
    {
        var order = NewOrder();

        order.MarkPaid(Now.AddMinutes(5));
        order.Ship(Now.AddHours(2));

        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Equal(Now.AddMinutes(5), order.PaidAt);
        Assert.Equal(Now.AddHours(2), order.ShippedAt);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Cannot_ship_an_unpaid_order()
    {
        var order = NewOrder();

        var error = Assert.Throws<DomainException>(() => order.Ship(Now));

        Assert.Contains("pending", error.Message, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Paid_order_can_be_cancelled()
    {
        var order = NewOrder();
        order.MarkPaid(Now);

        order.Cancel(Now.AddMinutes(1));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Now.AddMinutes(1), order.CancelledAt);
    }

    [Fact]
    public void Shipped_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.MarkPaid(Now);
        order.Ship(Now);

        Assert.Throws<DomainException>(() => order.Cancel(Now));
    }
}
