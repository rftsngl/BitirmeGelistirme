using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace WindowsAiAssistant.Infrastructure.Secrets;

/// <summary>
/// Windows DPAPI-protected per-profile secret files (CurrentUser scope only).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiModelProviderSecretStore : IModelProviderSecretStore
{
    private readonly string _secretsDirectory;

    public DpapiModelProviderSecretStore(string? secretsDirectory = null)
    {
        _secretsDirectory = secretsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "secrets");
    }

    public void SaveApiKey(string profileId, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(secret);

        Directory.CreateDirectory(_secretsDirectory);
        var path = GetSecretFilePath(profileId);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tempPath, protectedBytes);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    public bool TryGetApiKey(string profileId, out string? secret)
    {
        secret = null;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        var path = GetSecretFilePath(profileId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plaintext = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            secret = Encoding.UTF8.GetString(plaintext);
            return !string.IsNullOrWhiteSpace(secret);
        }
        catch
        {
            secret = null;
            return false;
        }
    }

    public void DeleteApiKey(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var path = GetSecretFilePath(profileId);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Fail-safe: caller can retry.
        }
    }

    public bool HasApiKey(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        var path = GetSecretFilePath(profileId);
        return File.Exists(path);
    }

    internal static string SanitizeProfileIdForFileName(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return "_empty";
        }

        var builder = new StringBuilder(profileId.Length);
        foreach (var c in profileId.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append('_');
            }
        }

        var s = builder.ToString();
        if (s.Length > 120)
        {
            s = s[..120];
        }

        return string.IsNullOrEmpty(s) ? "_empty" : s;
    }

    private string GetSecretFilePath(string profileId)
    {
        var safe = SanitizeProfileIdForFileName(profileId);
        return Path.Combine(_secretsDirectory, safe + ".bin");
    }
}
