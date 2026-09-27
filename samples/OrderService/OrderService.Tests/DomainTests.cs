using OrderService.Domain;

public class PricingTests
{
    static Order NewOrder(params LineItem[] items)
    {
        var o = new Order { Id = 1, Customer = new(1, "Test", new Address("s", "c", "1", "us")) };
        o.Items.AddRange(items);
        return o;
    }

    [Fact]
    public void Subtotal_SumsLineItems() =>
        Assert.Equal(25m, Pricing.Subtotal(NewOrder(new LineItem("A", 10m, 2), new LineItem("B", 5m, 1))));

    [Fact]
    public void ApplyDiscounts_PercentThenFixed() =>
        Assert.Equal(85m, Pricing.ApplyDiscounts(100m, [new("TEN", Fixed: 5m), new("PCT", Percent: 10m)]));

    [Fact]
    public void InvoiceTotal_AddsTax() =>
        Assert.Equal(21m, Pricing.InvoiceTotal(NewOrder(new LineItem("A", 10m, 2))));

    [Fact]
    public void InvoiceTotal_RoundsToCents() =>
        Assert.Equal(3.50m, Pricing.InvoiceTotal(NewOrder(new LineItem("A", 3.33m, 1))));
}

public class PagingTests
{
    static readonly int[] Twenty = Enumerable.Range(1, 20).ToArray();

    [Fact]
    public void FirstPage() => Assert.Equal([1, 2, 3, 4, 5], Paging.Paginate(Twenty, 1, 5).Items);

    [Fact]
    public void SecondPage() => Assert.Equal([6, 7, 8, 9, 10], Paging.Paginate(Twenty, 2, 5).Items);

    [Fact]
    public void TotalPages_ExactMultiple() => Assert.Equal(4, Paging.Paginate(Twenty, 1, 5).TotalPages);

    [Fact]
    public void RejectsPageZero() => Assert.Throws<ArgumentOutOfRangeException>(() => Paging.Paginate(Twenty, 0, 5));
}

public class ShippingTests
{
    [Fact]
    public void Label_FormatsAddress() =>
        Assert.Equal("Ada\n1 Main St\n12345 Springfield\nUS",
            Shipping.Label(new(1, "Ada", new Address("1 Main St", "Springfield", "12345", "us"))));

    [Fact]
    public void Cost_FreeOverThreshold()
    {
        Assert.Equal(0m, Shipping.Cost(50m));
        Assert.Equal(4.99m, Shipping.Cost(49.99m));
    }
}

public class QuantityParserTests
{
    [Theory]
    [InlineData("3", 3)]
    [InlineData(" 12 ", 12)]
    public void ParsesWholeNumbers(string raw, int expected) => Assert.Equal(expected, QuantityParser.Parse(raw));

    [Fact]
    public void RejectsZero() => Assert.Throws<FormatException>(() => QuantityParser.Parse("0"));
}
