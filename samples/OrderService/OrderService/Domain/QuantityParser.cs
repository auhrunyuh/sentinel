using System.Globalization;

namespace OrderService.Domain;

/// <summary>Parses quantities from CSV imports and query strings, e.g. "3" or "1.5" (kg).</summary>
public static class QuantityParser
{
    public static decimal Parse(string raw)
    {
        var q = decimal.Parse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
        if (q <= 0) throw new FormatException($"quantity must be positive: '{raw}'");
        return q;
    }
}
