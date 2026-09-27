using OrderService.Domain;

public class Bug05Tests
{
    [Fact]
    public void StackedDiscounts_NeverGoNegative() =>
        Assert.Equal(0m, Pricing.ApplyDiscounts(10m, [new Discount("A", Fixed: 5m), new Discount("B", Fixed: 8m)]));
}
