using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Orders.Api.Features.Orders;

namespace Orders.Api.Tests;

public sealed class OrderEndpointsTests(OrdersApiFactory factory) : IClassFixture<OrdersApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UniqueEmail() => $"customer-{Guid.NewGuid():N}@example.com";

    private static CreateOrderRequest NewOrder(string email) => new(email, [new CreateOrderLineRequest("SKU-1", 1, 10m)]);

    private async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _client.PostAsJsonAsync("/api/orders", request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Ct))!;
    }

    private async Task<HttpResponseMessage> PostActionAsync(long id, string action) =>
        await _client.PostAsync($"/api/orders/{id}/{action}", content: null, Ct);

    [Fact]
    public async Task Create_returns_201_with_location_and_merged_lines()
    {
        var request = new CreateOrderRequest(
            "Buyer@Example.com",
            [new("kb-01", 1, 49.90m), new("KB-01", 2, 49.90m), new("mouse", 1, 25m)]);

        var response = await _client.PostAsJsonAsync("/api/orders", request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(Ct);
        Assert.NotNull(order);
        Assert.EndsWith($"/api/orders/{order.Id}", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Equal("buyer@example.com", order.CustomerEmail);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(174.70m, order.Total);
    }

    [Fact]
    public async Task Create_with_invalid_payload_returns_validation_problem()
    {
        var request = new CreateOrderRequest("not-an-email", [new("", 0, -1m)]);

        var response = await _client.PostAsJsonAsync("/api/orders", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Contains("customerEmail", problem.Errors.Keys);
        Assert.Contains("lines[0]", problem.Errors.Keys);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_order()
    {
        var response = await _client.GetAsync("/api/orders/999999", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_can_be_paid_and_shipped_but_not_cancelled_afterwards()
    {
        var order = await CreateOrderAsync(NewOrder(UniqueEmail()));

        var paid = await (await PostActionAsync(order.Id, "pay")).Content.ReadFromJsonAsync<OrderResponse>(Ct);
        Assert.Equal("Paid", paid!.Status);
        Assert.NotNull(paid.PaidAt);

        var shipped = await (await PostActionAsync(order.Id, "ship")).Content.ReadFromJsonAsync<OrderResponse>(Ct);
        Assert.Equal("Shipped", shipped!.Status);

        var cancel = await PostActionAsync(order.Id, "cancel");
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        var problem = await cancel.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Contains("shipped", problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shipping_an_unpaid_order_returns_409()
    {
        var order = await CreateOrderAsync(NewOrder(UniqueEmail()));

        var response = await PostActionAsync(order.Id, "ship");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_customer_and_status()
    {
        var email = UniqueEmail();
        var first = await CreateOrderAsync(NewOrder(email));
        await CreateOrderAsync(NewOrder(email));
        await CreateOrderAsync(NewOrder(UniqueEmail()));
        await PostActionAsync(first.Id, "pay");

        var all = await _client.GetFromJsonAsync<PagedResponse<OrderResponse>>($"/api/orders?customerEmail={email}", Ct);
        var paid = await _client.GetFromJsonAsync<PagedResponse<OrderResponse>>($"/api/orders?customerEmail={email}&status=Paid", Ct);

        Assert.Equal(2, all!.Total);
        Assert.Equal(first.Id, Assert.Single(paid!.Items).Id);
    }

    [Theory]
    [InlineData("/api/orders?limit=500")]
    [InlineData("/api/orders?offset=-1")]
    [InlineData("/api/orders?status=Unknown")]
    public async Task List_rejects_invalid_query_parameters(string url)
    {
        var response = await _client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
