using OrderService.Domain;

public class Bug03Tests
{
    [Fact]
    public void InvoiceTotal_RoundsHalfCentAwayFromZero()
    {
        var o = new Order { Id = 1, Customer = new(1, "T", null) };
        o.Items.Add(new LineItem("A", 0.50m, 1)); // 0.50 * 1.05 = 0.525
        Assert.Equal(0.53m, Pricing.InvoiceTotal(o));
    }
}
