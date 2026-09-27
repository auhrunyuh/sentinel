using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentinel.Core;

public enum Severity { Info, Low, Medium, High, Critical }

/// <param name="Source">scanner | security | performance</param>
public sealed record Finding(string Source, string Rule, Severity Severity, string File, int Line, string Message, string? Fix = null);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
