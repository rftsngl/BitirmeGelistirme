using System.Security.Cryptography;
using System.Text;

namespace WindowsAiAssistant.App.Services;

public static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string plainText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string stored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stored);
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return stored;
        }

        var payload = stored[Prefix.Length..];
        var protectedBytes = Convert.FromBase64String(payload);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
