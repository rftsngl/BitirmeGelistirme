using System.Runtime.InteropServices;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class CredentialStoreService : ICredentialStoreService
{
    public ActionResult Execute(string mode, string? target = null, string? username = null, string? secret = null)
    {
        var normalized = (mode ?? "list").Trim().ToLowerInvariant();
        return normalized switch
        {
            "list" => List(target),
            "read" => Read(target),
            "store" => Store(target, username, secret),
            "delete" => Delete(target),
            _ => IntegrationResultHelper.Fail("Desteklenen modlar: list, read, store, delete.")
        };
    }

    private static ActionResult List(string? filter)
    {
        try
        {
            if (!CredEnumerate(null, 0, out var count, out var credentialsPtr))
            {
                return IntegrationResultHelper.Fail($"Credential listelenemedi: {Marshal.GetLastWin32Error()}");
            }

            var builder = new StringBuilder();
            var seen = 0;
            try
            {
                var size = IntPtr.Size;
                for (var i = 0; i < count; i++)
                {
                    var credPtr = Marshal.ReadIntPtr(credentialsPtr, i * size);
                    var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
                    var targetName = Marshal.PtrToStringUni(cred.TargetName) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(filter) &&
                        !targetName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    seen++;
                    var user = cred.UserName != IntPtr.Zero ? Marshal.PtrToStringUni(cred.UserName) : string.Empty;
                    builder.AppendLine($"{targetName} | user={user} | type={cred.Type}");
                }
            }
            finally
            {
                CredFree(credentialsPtr);
            }

            builder.Insert(0, $"count={seen}\n");
            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Credential listesi alinamadi: {ex.Message}");
        }
    }

    private static ActionResult Read(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return IntegrationResultHelper.Fail("read icin target gerekli.");
        }

        if (!CredRead(target, CRED_TYPE.GENERIC, 0, out var credPtr))
        {
            return IntegrationResultHelper.Fail($"Credential okunamadi: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            var user = cred.UserName != IntPtr.Zero ? Marshal.PtrToStringUni(cred.UserName) : string.Empty;
            var secret = cred.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2)
                : string.Empty;
            return IntegrationResultHelper.Ok($"target={target}\nuser={user}\nsecretLength={secret?.Length ?? 0}");
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    private static ActionResult Store(string? target, string? username, string? secret)
    {
        if (string.IsNullOrWhiteSpace(target) || secret is null)
        {
            return IntegrationResultHelper.Fail("store icin target ve secret gerekli.");
        }

        var user = username ?? Environment.UserName;
        var blob = Encoding.Unicode.GetBytes(secret);
        var cred = new NativeCredential
        {
            Type = CRED_TYPE.GENERIC,
            TargetName = Marshal.StringToCoTaskMemUni(target),
            UserName = Marshal.StringToCoTaskMemUni(user),
            CredentialBlob = Marshal.AllocCoTaskMem(blob.Length),
            CredentialBlobSize = blob.Length,
            Persist = CRED_PERSIST.LOCAL_MACHINE,
            AttributeCount = 0
        };

        Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);

        try
        {
            if (!CredWrite(ref cred, 0))
            {
                return IntegrationResultHelper.Fail($"Credential yazilamadi: {Marshal.GetLastWin32Error()}");
            }

            return IntegrationResultHelper.Ok($"Credential kaydedildi: {target}");
        }
        finally
        {
            Marshal.FreeCoTaskMem(cred.TargetName);
            Marshal.FreeCoTaskMem(cred.UserName);
            Marshal.FreeCoTaskMem(cred.CredentialBlob);
        }
    }

    private static ActionResult Delete(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return IntegrationResultHelper.Fail("delete icin target gerekli.");
        }

        if (!CredDelete(target, CRED_TYPE.GENERIC, 0))
        {
            return IntegrationResultHelper.Fail($"Credential silinemedi: {Marshal.GetLastWin32Error()}");
        }

        return IntegrationResultHelper.Ok($"Credential silindi: {target}");
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    private enum CRED_TYPE : uint
    {
        GENERIC = 1
    }

    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public CRED_TYPE Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
