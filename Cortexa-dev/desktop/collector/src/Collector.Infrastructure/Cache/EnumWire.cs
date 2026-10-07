using System.Text;

namespace Collector.Infrastructure.Cache;

internal static class EnumWire
{
    public static TEnum FromWire<TEnum>(string value)
        where TEnum : struct, Enum
    {
        var builder = new StringBuilder(value.Length);
        var capitalizeNext = true;
        foreach (var c in value)
        {
            if (c == '_')
            {
                capitalizeNext = true;
                continue;
            }

            builder.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
            capitalizeNext = false;
        }

        return Enum.Parse<TEnum>(builder.ToString());
    }
}
