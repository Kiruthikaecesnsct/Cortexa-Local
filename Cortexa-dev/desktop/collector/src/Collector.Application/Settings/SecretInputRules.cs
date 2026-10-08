namespace Collector.Application.Settings;

public static class SecretInputRules
{
    public const int MaxLength = 512;

    public static string? Validate(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return "Enter a value.";
        }

        if (value.Length > MaxLength)
        {
            return $"The value is longer than {MaxLength} characters.";
        }

        return value.Any(char.IsWhiteSpace) ? "The value must not contain spaces." : null;
    }

    public static string Normalize(string raw)
    {
        var reason = Validate(raw);
        return reason is null ? raw.Trim() : throw new ArgumentException(reason, nameof(raw));
    }
}
