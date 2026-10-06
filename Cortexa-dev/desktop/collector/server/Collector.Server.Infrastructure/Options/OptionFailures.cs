using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Options;

internal sealed class OptionFailures
{
    private readonly List<string> _failures = [];

    public void RequireText(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _failures.Add($"{optionName} is required.");
        }
    }

    public void RequirePositive(int value, string optionName)
    {
        if (value <= 0)
        {
            _failures.Add($"{optionName} must be greater than zero.");
        }
    }

    public void RequireAbsoluteUri(string? value, string optionName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            _failures.Add($"{optionName} must be an absolute URI.");
        }
    }

    public void RequireEither(string? first, string? second, string optionName)
    {
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second))
        {
            _failures.Add($"{optionName} is required.");
        }
    }

    public ValidateOptionsResult ToResult() =>
        _failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(_failures);
}
