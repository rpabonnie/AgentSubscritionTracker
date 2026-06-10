// SPEC-0002 — production ICopilotCredentialStore: read-only Windows Credential Manager
// access via advapi32 CredReadW/CredFree (CRED_TYPE_GENERIC). The secret is decoded in
// memory only and never logged, cached, or persisted.

using System.Runtime.InteropServices;
using System.Text;

namespace AgentSubscriptionTracker.App.Services;

/// <summary>
/// Reads a generic credential blob from Windows Credential Manager. Returns null when
/// the credential does not exist or cannot be read; never throws for lookup failures.
/// </summary>
public sealed partial class WindowsCredentialStore : ICopilotCredentialStore
{
    private const uint CredTypeGeneric = 1;

    /// <inheritdoc />
    public string? ReadSecret(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!NativeMethods.CredRead(serviceName, CredTypeGeneric, 0, out var credentialPtr) ||
            credentialPtr == 0)
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CredentialW>(credentialPtr);
            if (credential.CredentialBlob == 0 || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            var secret = DecodeBlob(blob);
            return string.IsNullOrWhiteSpace(secret) ? null : secret;
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    /// <summary>
    /// Credential blobs carry no encoding metadata. ASCII tokens stored as UTF-16 contain
    /// interleaved zero bytes; anything without zero bytes is treated as UTF-8.
    /// </summary>
    private static string DecodeBlob(byte[] blob)
    {
        var looksUtf16 = blob.Length % 2 == 0 && Array.IndexOf(blob, (byte)0) >= 0;
        var decoded = looksUtf16 ? Encoding.Unicode.GetString(blob) : Encoding.UTF8.GetString(blob);
        return decoded.TrimEnd('\0').Trim();
    }

    /// <summary>P/Invoke surface (CA1060: isolated in a NativeMethods class).</summary>
    private static partial class NativeMethods
    {
        [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CredRead(
            string targetName, uint type, uint reservedFlag, out nint credentialPtr);

        [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial void CredFree(nint credential);

        /// <summary>Mirror of the native CREDENTIALW layout (FILETIME as two DWORDs).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct CredentialW
        {
            internal uint Flags;
            internal uint Type;
            internal nint TargetName;
            internal nint Comment;
            internal uint LastWrittenLow;
            internal uint LastWrittenHigh;
            internal uint CredentialBlobSize;
            internal nint CredentialBlob;
            internal uint Persist;
            internal uint AttributeCount;
            internal nint Attributes;
            internal nint TargetAlias;
            internal nint UserName;
        }
    }
}
