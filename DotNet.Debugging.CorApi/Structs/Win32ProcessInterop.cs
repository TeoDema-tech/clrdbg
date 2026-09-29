// Windows process creation structures and interop for Desktop CLR debugging.
// Used by ICorDebug.TryCreateProcess when launching .NET Framework 4.x applications.

using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi;

[StructLayout(LayoutKind.Sequential)]
public struct StartupInfoW {
    public int cb;
    public nint lpReserved;
    public nint lpDesktop;
    public nint lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public nint lpReserved2;
    public nint hStdInput;
    public nint hStdOutput;
    public nint hStdError;
}

[StructLayout(LayoutKind.Sequential)]
public struct ProcessInformation {
    public nint hProcess;
    public nint hThread;
    public uint dwProcessId;
    public uint dwThreadId;
}

public static partial class Win32NativeMethods {
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);
}
