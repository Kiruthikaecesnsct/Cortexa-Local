using System.Net.Mail;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Validation;

internal static class UserFieldValidator
{
    private const int MinPasswordLength = 8;
    private const int MinUsernameLength = 2;
    private const int MaxUsernameLength = 100;

    internal static void EnsureValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
            throw new BadRequestException("A valid email address is required");
    }

    internal static void EnsureValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new BadRequestException("Username must not be empty");

        var trimmed = username.Trim();
        if (trimmed.Length < MinUsernameLength || trimmed.Length > MaxUsernameLength)
            throw new BadRequestException($"Username must be between {MinUsernameLength} and {MaxUsernameLength} characters");
    }

    internal static void EnsurePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            throw new BadRequestException("Password must be at least 8 characters");
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
