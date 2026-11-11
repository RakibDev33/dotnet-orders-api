using System.Net.Mail;
using Orders.Api.Domain;

namespace Orders.Api.Features.Orders;

public sealed record CreateOrderLineRequest(string Sku, int Quantity, decimal UnitPrice);

public sealed record CreateOrderRequest(string CustomerEmail, IReadOnlyList<CreateOrderLineRequest> Lines)
{
    /// <summary>Validates the request shape and returns errors keyed by JSON path.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(CustomerEmail) || !MailAddress.TryCreate(CustomerEmail, out _))
        {
            errors["customerEmail"] = ["A valid email address is required."];
        }

        if (Lines is null || Lines.Count == 0)
        {
            errors["lines"] = ["At least one order line is required."];
            return errors;
        }

        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (line is null)
            {
                errors[$"lines[{i}]"] = ["Order line is required."];
                continue;
            }

            var lineErrors = new List<string>();
            if (string.IsNullOrWhiteSpace(line.Sku))
            {
                lineErrors.Add("SKU is required.");
            }

            if (line.Quantity is < 1 or > OrderLine.MaxQuantity)
            {
                lineErrors.Add($"Quantity must be between 1 and {OrderLine.MaxQuantity}.");
            }

            if (line.UnitPrice < 0)
            {
                lineErrors.Add("Unit price cannot be negative.");
            }

            if (lineErrors.Count > 0)
            {
                errors[$"lines[{i}]"] = [.. lineErrors];
            }
        }

        var conflictingSkus = Lines
            .Where(line => line is not null && !string.IsNullOrWhiteSpace(line.Sku))
            .GroupBy(line => line.Sku.Trim().ToUpperInvariant())
            .Where(group => group.Select(line => line.UnitPrice).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (conflictingSkus.Count > 0)
        {
            errors["lines"] = [$"Conflicting unit prices for SKU(s): {string.Join(", ", conflictingSkus)}."];
        }

        return errors;
    }
}

public sealed record OrderLineResponse(string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderResponse(
    long Id,
    string CustomerEmail,
    string Status,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? CancelledAt)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerEmail,
        order.Status.ToString(),
        order.Total,
        [.. order.Lines.Select(line => new OrderLineResponse(line.Sku, line.Quantity, line.UnitPrice, line.LineTotal))],
        order.CreatedAt,
        order.PaidAt,
        order.ShippedAt,
        order.CancelledAt);
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Limit, int Offset);
