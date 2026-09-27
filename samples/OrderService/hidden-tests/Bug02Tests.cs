using OrderService.Domain;

public class Bug02Tests
{
    [Fact]
    public void Label_CustomerWithoutAddress_IsPickup() =>
        Assert.Contains("PICKUP", Shipping.Label(new Customer(2, "Pat", null)));
}
