using System.Text;
using Microsoft.Win32;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class RegistryOperationService : IRegistryOperationService
{
    public ActionResult Execute(string mode, string? hive = null, string? path = null, string? name = null, string? value = null, string? kind = null)
    {
        var normalized = (mode ?? "read").Trim().ToLowerInvariant();
        return normalized switch
        {
            "read" => Read(hive, path, name),
            "write" => Write(hive, path, name, value, kind),
            "delete" => Delete(hive, path, name),
            _ => IntegrationResultHelper.Fail($"Desteklenen modlar: read, write, delete. Verilen: {mode}")
        };
    }

    private static ActionResult Read(string? hive, string? path, string? name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return IntegrationResultHelper.Fail("read icin parameters.path gerekli.");
        }

        try
        {
            using var key = OpenKey(hive, path, writable: false);
            if (key is null)
            {
                return IntegrationResultHelper.Fail($"Registry anahtari bulunamadi: {path}");
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var val = key.GetValue(name.Trim());
                return IntegrationResultHelper.Ok($"{hive ?? "HKCU"}\\{path} [{name}] = {FormatValue(val)}");
            }

            var builder = new StringBuilder();
            builder.AppendLine($"{hive ?? "HKCU"}\\{path}");
            foreach (var valueName in key.GetValueNames())
            {
                builder.AppendLine($"{valueName}={FormatValue(key.GetValue(valueName))}");
            }

            foreach (var subKey in key.GetSubKeyNames().Take(50))
            {
                builder.AppendLine($"[subkey] {subKey}");
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Registry okunamadi: {ex.Message}");
        }
    }

    private static ActionResult Write(string? hive, string? path, string? name, string? value, string? kind)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name) || value is null)
        {
            return IntegrationResultHelper.Fail("write icin path, name ve value gerekli.");
        }

        try
        {
            using var key = OpenKey(hive, path, writable: true, create: true);
            if (key is null)
            {
                return IntegrationResultHelper.Fail($"Registry anahtari acilamadi: {path}");
            }

            var registryKind = (kind ?? "string").Trim().ToLowerInvariant() switch
            {
                "dword" or "int" => RegistryValueKind.DWord,
                "qword" or "long" => RegistryValueKind.QWord,
                "expand" => RegistryValueKind.ExpandString,
                _ => RegistryValueKind.String
            };

            object parsed = registryKind switch
            {
                RegistryValueKind.DWord => int.Parse(value),
                RegistryValueKind.QWord => long.Parse(value),
                _ => value
            };

            key.SetValue(name.Trim(), parsed, registryKind);
            return IntegrationResultHelper.Ok($"Yazildi: {hive ?? "HKCU"}\\{path} [{name}]");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Registry yazilamadi: {ex.Message}");
        }
    }

    private static ActionResult Delete(string? hive, string? path, string? name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return IntegrationResultHelper.Fail("delete icin path gerekli.");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                var parentPath = Path.GetDirectoryName(path.Replace('/', '\\'))?.Replace('\\', '/');
                var keyName = Path.GetFileName(path.Replace('/', '\\'));
                if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(keyName))
                {
                    return IntegrationResultHelper.Fail("Alt anahtar silmek icin tam path gerekli.");
                }

                using var parent = OpenKey(hive, parentPath, writable: true);
                parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
                return IntegrationResultHelper.Ok($"Alt anahtar silindi: {path}");
            }

            using var key = OpenKey(hive, path, writable: true);
            key?.DeleteValue(name.Trim(), throwOnMissingValue: false);
            return IntegrationResultHelper.Ok($"Deger silindi: {path} [{name}]");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Registry silinemedi: {ex.Message}");
        }
    }

    private static RegistryKey? OpenKey(string? hive, string path, bool writable, bool create = false)
    {
        var root = ResolveHive(hive);
        var normalized = path.Trim().TrimStart('\\');
        return create
            ? root.CreateSubKey(normalized, writable)
            : root.OpenSubKey(normalized, writable);
    }

    private static RegistryKey ResolveHive(string? hive) =>
        (hive ?? "HKCU").Trim().ToUpperInvariant() switch
        {
            "HKLM" or "LOCALMACHINE" => Registry.LocalMachine,
            "HKCR" or "CLASSESROOT" => Registry.ClassesRoot,
            "HKU" or "USERS" => Registry.Users,
            "HKCC" or "CURRENTCONFIG" => Registry.CurrentConfig,
            _ => Registry.CurrentUser
        };

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "(null)",
            byte[] bytes => Convert.ToHexString(bytes),
            _ => value.ToString() ?? string.Empty
        };
}
