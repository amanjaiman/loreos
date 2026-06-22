using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lore.Agent.Capture;

/// <summary>The Win32 implementation of <see cref="IForegroundWindowSource"/> — the
/// one place in the agent that P/Invokes for window identity (constitution §3.2). It is
/// deliberately thin and total: any failure to read a window is reported as
/// <see cref="WindowSnapshot.None"/> rather than thrown, so a transient desktop state
/// never disturbs the capture loop. The dwell and change logic that this feeds lives in
/// the testable <see cref="WindowMonitor"/>.</summary>
public sealed partial class Win32ForegroundWindowSource : IForegroundWindowSource
{
    public WindowSnapshot Current()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return WindowSnapshot.None;
        }

        string title = ReadTitle(handle);
        string executable = ReadExecutable(handle);
        return new WindowSnapshot(handle.ToInt64(), executable, title);
    }

    private static string ReadTitle(IntPtr handle)
    {
        int length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        int copied = GetWindowText(handle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    private static string ReadExecutable(IntPtr handle)
    {
        if (GetWindowThreadProcessId(handle, out uint pid) == 0 || pid == 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName; // base executable name, e.g. "chrome" — no path, no key material
        }
        catch (ArgumentException)
        {
            return string.Empty; // the process exited between the handle read and here
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty; // access denied for elevated processes (Task Manager, UAC dialogs)
        }
    }

    // Pin every import to the system directory so a planted user32.dll on the search
    // path cannot be loaded in its place (CA5392 — DLL-hijacking defense).
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // EntryPoint is required: LibraryImport binds the exact name, and user32 exports only the
    // W/A variants (GetWindowTextLengthW/A), never a bare GetWindowTextLength — unlike the old
    // DllImport, it does not auto-append the charset suffix. Without this the P/Invoke throws
    // EntryPointNotFoundException on every poll and capture silently produces nothing.
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);
}
