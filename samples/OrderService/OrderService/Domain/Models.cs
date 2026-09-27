namespace OrderService.Domain;

public sealed record Address(string Street, string City, string PostalCode, string Country);

public sealed record Customer(int Id, string Name, Address? Address);

public sealed record LineItem(string Sku, decimal UnitPrice, decimal Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}

/// <summary>Percent is 0..100; Fixed is a currency amount taken off after percentages.</summary>
public sealed record Discount(string Code, decimal Percent = 0, decimal Fixed = 0);

public sealed class Order
{
    public required int Id { get; init; }
    public required Customer Customer { get; init; }
    public List<LineItem> Items { get; } = [];
    public List<Discount> Discounts { get; } = [];
}
