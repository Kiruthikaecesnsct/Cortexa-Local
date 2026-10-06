using System.Text.Json;

namespace Collector.Domain.Serialization;

public static class WireName
{
    public static string ToWire<TEnum>(this TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
