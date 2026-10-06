namespace Collector.Server.Api.Endpoints;

public static class IdempotencyKeys
{
    public const string HeaderName = "Idempotency-Key";

    private const char FirstPrintable = ' ';
    private const char LastPrintable = '~';

    public static bool TryRead(HttpRequest request, int maxLength, out string key)
    {
        key = string.Empty;
        var values = request.Headers[HeaderName];
        if (values.Count != 1)
        {
            return false;
        }

        var candidate = values[0];
        if (!IsValid(candidate, maxLength))
        {
            return false;
        }

        key = candidate!;
        return true;
    }

    private static bool IsValid(string? candidate, int maxLength) =>
        !string.IsNullOrWhiteSpace(candidate)
        && candidate.Length <= maxLength
        && candidate.All(IsPrintableAscii);

    private static bool IsPrintableAscii(char value) => value is >= FirstPrintable and <= LastPrintable;
}
