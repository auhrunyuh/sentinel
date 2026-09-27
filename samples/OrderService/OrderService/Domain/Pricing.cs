namespace OrderService.Domain;

public static class Pricing
{
    public const decimal TaxRate = 0.05m;

    public static decimal Subtotal(Order order) => order.Items.Sum(i => i.Total);

    public static decimal ApplyDiscounts(decimal subtotal, IEnumerable<Discount> discounts)
    {
        var total = subtotal;
        foreach (var d in discounts.OrderBy(d => d.Fixed)) // percentages first, then fixed amounts
            total = total * (1 - d.Percent / 100m) - d.Fixed;
        return Math.Max(0m, total);
    }

    public static decimal RoundMoney(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal InvoiceTotal(Order order)
    {
        var net = ApplyDiscounts(Subtotal(order), order.Discounts);
        return RoundMoney(net * (1 + TaxRate));
    }
}
