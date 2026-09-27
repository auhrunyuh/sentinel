using System.Globalization;
using OrderService.Domain;

public class Bug04Tests
{
    [Fact]
    public void Parse_IsCultureInvariant()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try { Assert.Equal(1.5m, QuantityParser.Parse("1.5")); }
        finally { CultureInfo.CurrentCulture = prev; }
    }
}
