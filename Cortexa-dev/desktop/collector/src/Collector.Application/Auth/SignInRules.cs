namespace Collector.Application.Auth;

public static class SignInRules
{
    public const int MaxEmailLength = 254;
    public const int MaxPasswordLength = 128;

    public static SignInOutcome.InvalidInput? Validate(string? email, string? password) =>
        ValidateEmail(email) ?? ValidatePassword(password);

    private static SignInOutcome.InvalidInput? ValidateEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new SignInOutcome.InvalidInput(SignInField.Email, "Enter your email.");
        }

        if (trimmed.Length > MaxEmailLength || !trimmed.Contains('@'))
        {
            return new SignInOutcome.InvalidInput(SignInField.Email, "Enter a valid email address.");
        }

        return null;
    }

    private static SignInOutcome.InvalidInput? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new SignInOutcome.InvalidInput(SignInField.Password, "Enter your password.");
        }

        return password.Length > MaxPasswordLength
            ? new SignInOutcome.InvalidInput(SignInField.Password, "Password is too long.")
            : null;
    }
}
