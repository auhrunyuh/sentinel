namespace OrderService.Domain;

public static class Shipping
{
    public const decimal FlatRate = 4.99m;
    public const decimal FreeOver = 50m;

    public static decimal Cost(decimal net) => net >= FreeOver ? 0m : FlatRate;

    public static string Label(Customer customer)
    {
        if (customer.Address is null) return $"{customer.Name}\nIN-STORE PICKUP";
        var a = customer.Address;
        return $"{customer.Name}\n{a.Street}\n{a.PostalCode} {a.City}\n{a.Country.ToUpperInvariant()}";
    }
}
