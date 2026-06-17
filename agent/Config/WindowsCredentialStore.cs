using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Lore.Agent.Config;

/// <summary>The Windows implementation of <see cref="ICredentialStore"/>: generic
/// credentials in the Windows Credential Manager, which encrypts the blob with DPAPI under
/// the user account (spec 004). The handle is the credential's target name, so a key the
/// user stored under <c>lore/provider</c> is visible (and removable) in Credential Manager
/// under that name. Every secret that passes through is registered with the
/// <see cref="SecretRegistry"/> so it can never surface in a log.</summary>
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxBlobSize = 5 * 512; // CRED_MAX_CREDENTIAL_BLOB_SIZE

    private readonly SecretRegistry _registry;

    public WindowsCredentialStore(SecretRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public string? Read(string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        if (!CredRead(handle, CredTypeGeneric, 0, out IntPtr credentialPtr))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound
                ? null
                : throw new Win32Exception(error, $"Reading credential '{handle}' from Credential Manager failed.");
        }

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            byte[] blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            string secret = Encoding.Unicode.GetString(blob);
            _registry.Register(secret);
            return secret;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public void Write(string handle, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] blob = Encoding.Unicode.GetBytes(secret);
        if (blob.Length > MaxBlobSize)
        {
            throw new ArgumentException(
                "Secret is too large for the Windows Credential Manager.", nameof(secret));
        }

        _registry.Register(secret);
        IntPtr targetName = Marshal.StringToHGlobalUni(handle);
        IntPtr blobPtr = blob.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(blob.Length);
        try
        {
            if (blobPtr != IntPtr.Zero)
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);
            }

            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
            };

            if (!CredWrite(in credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(), $"Writing credential '{handle}' to Credential Manager failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(targetName);
            if (blobPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blobPtr);
            }
        }
    }

    public void Delete(string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        if (!CredDelete(handle, CredTypeGeneric, 0))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(
                    error, $"Deleting credential '{handle}' from Credential Manager failed.");
            }
        }
    }

    // Mirrors the Win32 CREDENTIALW layout. Strings and the blob are native pointers we
    // marshal by hand, so the struct itself is blittable. Fields Lore does not set are
    // kept for layout fidelity.
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    // Pin to System32 to defeat DLL hijacking (CA5392), matching the capture pipeline's
    // Win32 seam.
    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in Credential credential, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, int type, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CredFree(IntPtr buffer);
}
