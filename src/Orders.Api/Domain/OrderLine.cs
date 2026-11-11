namespace Orders.Api.Domain;

public sealed class OrderLine
{
    public const int MaxQuantity = 1_000;

    private OrderLine()
    {
        Sku = string.Empty;
    }

    public OrderLine(string sku, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainException("SKU is required.");
        }

        if (quantity is < 1 or > MaxQuantity)
        {
            throw new DomainException($"Quantity must be between 1 and {MaxQuantity}.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("Unit price cannot be negative.");
        }

        Sku = sku.Trim().ToUpperInvariant();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Sku { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal => Quantity * UnitPrice;

    internal void IncreaseQuantity(int quantity)
    {
        if (Quantity + quantity > MaxQuantity)
        {
            throw new DomainException($"Quantity for SKU '{Sku}' cannot exceed {MaxQuantity}.");
        }

        Quantity += quantity;
    }
}
